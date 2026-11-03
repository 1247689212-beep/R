using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input.Custom;

[assembly: Guid("1155f63f-360b-4768-9627-d949682246d9")]

namespace WoodFilletR13
{
    // Rhino discovers this public plugin class through the assembly GUID.
    public sealed class WoodFilletPlugin : Rhino.PlugIns.PlugIn
    {
        public WoodFilletPlugin() { }
    }

    [Guid("6c8a2114-6163-4444-a879-cb02bd09d32a")]
    public sealed class WoodFilletCommand : Command
    {
        public override string EnglishName => "WoodFilletR13";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            // All design dimensions are in mm, converted to document units.
            if (doc.ModelUnitSystem == UnitSystem.None) return Result.Failure;
            double scale = RhinoMath.UnitScale(UnitSystem.Millimeters, doc.ModelUnitSystem);
            double radius = 1.3 * scale;
            double tol = doc.ModelAbsoluteTolerance;
            if (tol <= 0 || tol > 0.01 * scale)
            {
                RhinoApp.WriteLine("请将绝对公差设置为不大于0.01mm的等效值。");
                return Result.Failure;
            }
            RhinoApp.WriteLine("仅用于实际4mm板件；脚本不自动测厚。请选择需要处理的一侧。");

            using (var go = new GetObject())
            {
                go.SetCommandPrompt("选择同一板件的实体棱边，Enter完成");
                go.GeometryFilter = ObjectType.Curve;
                go.GeometryAttributeFilter = GeometryAttributeFilter.EdgeCurve;
                go.SubObjectSelect = true;
                go.EnablePreSelect(false, true);
                go.GetMultiple(1, 0);
                if (go.CommandResult() != Result.Success) return go.CommandResult();

                // Collect all indices before computing; fillets change topology.
                Guid sourceId = go.Object(0).ObjectId;
                var indices = new HashSet<int>();
                for (int i = 0; i < go.ObjectCount; ++i)
                {
                    var reference = go.Object(i);
                    var edge = reference.Edge();
                    if (reference.ObjectId != sourceId || edge == null ||
                        edge.Valence != EdgeAdjacency.Interior)
                    {
                        RhinoApp.WriteLine("请选择同一实体的双面公共棱边。");
                        return Result.Failure;
                    }
                    indices.Add(edge.EdgeIndex);
                }
                var source = doc.Objects.FindId(sourceId);
                var brep = source == null ? null : source.Geometry as Brep;
                if (brep == null || !brep.IsValid || !brep.IsSolid)
                {
                    RhinoApp.WriteLine("需要有效封闭Brep；轻量拉伸先ConvertExtrusion。");
                    return Result.Failure;
                }

                // Preserve the fixed radius; never silently reduce it on failure.
                Brep[] results;
                using (var copy = brep.DuplicateBrep())
                {
                    int[] edges = indices.OrderBy(x => x).ToArray();
                    double[] radii = Enumerable.Repeat(radius, edges.Length).ToArray();
                    try
                    {
                        results = Brep.CreateFilletEdges(copy, edges, radii, radii,
                            BlendType.Fillet, RailType.RollingBall, tol);
                    }
                    catch (Exception e)
                    {
                        RhinoApp.WriteLine("圆角失败，原件保留：" + e.Message);
                        return Result.Failure;
                    }
                }
                if (results == null) return Result.Failure;
                try
                {
                    // Reject split or open results instead of writing broken geometry.
                    if (results.Length != 1 || results[0] == null ||
                        !results[0].IsValid || !results[0].IsSolid)
                    {
                        RhinoApp.WriteLine("未得到唯一有效封闭实体；原件保留。");
                        return Result.Failure;
                    }
                    var attributes = source.Attributes.Duplicate();
                    attributes.Name = (attributes.Name ?? "Wood") + "_R1.3mm";
                    attributes.SetUserString("FilletRadius_mm", "1.3");
                    Guid id = doc.Objects.AddBrep(results[0], attributes);
                    if (id == Guid.Empty) return Result.Failure;
                    // Hide only after successful addition. Rhino command supports Undo.
                    doc.Objects.Hide(sourceId, true);
                    doc.Objects.UnselectAll();
                    doc.Objects.Select(id);
                    doc.Views.Redraw();
                    RhinoApp.WriteLine("完成R1.3圆角。原件已隐藏，用Show恢复。");
                    return Result.Success;
                }
                finally
                {
                    foreach (var result in results) if (result != null) result.Dispose();
                }
            }
        }
    }
}
