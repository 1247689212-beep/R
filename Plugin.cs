using System;
using System.Runtime.InteropServices;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
[assembly: Guid("1155f63f-360b-4768-9627-d949682246d9")]
namespace WoodFilletR13 {
 public sealed class WoodFilletPlugin : Rhino.PlugIns.PlugIn {}
 [Guid("6c8a2114-6163-4444-a879-cb02bd09d32a")]
 public sealed class WoodFilletCommand : Command {
  public override string EnglishName { get { return "WoodFilletR13"; } }
  static Curve Slot(RhinoDoc doc, Plane p, double w, double h, double r) {
   using(var rect = new Rectangle3d(p,w,h).ToNurbsCurve()) {
    var c=Curve.CreateFilletCornersCurve(rect,r,doc.ModelAbsoluteTolerance,doc.ModelAngleToleranceRadians);
    if(c!=null && c.IsValid && c.IsClosed) return c;
    if(c!=null)c.Dispose();
    return null;
   }
  }
  protected override Result RunCommand(RhinoDoc doc, RunMode mode) {
   if(doc.ModelUnitSystem==UnitSystem.None || doc.ModelUnitSystem==UnitSystem.CustomUnits) return Result.Failure;
   double s=RhinoMath.UnitScale(UnitSystem.Millimeters,doc.ModelUnitSystem);
   double w=4*s,r=1.3*s,tol=doc.ModelAbsoluteTolerance;
   if(s<=0 || double.IsNaN(s) || double.IsInfinity(s) || tol<=0 || tol>0.01*s) {
    RhinoApp.WriteLine("请设置标准长度单位，绝对公差不大于0.01mm。"); return Result.Failure;
   }
   using(var opt=new GetOption()) {
    opt.SetCommandPrompt("二维卡口：New新建，Convert转换矩形");
    int n=opt.AddOption("New"); opt.AddOption("Convert");
    if(opt.Get()!=GetResult.Option)return opt.CommandResult();
    if(opt.OptionIndex()==n) return NewSlot(doc,s,w,r);
   }
   using(var go=new GetObject()) {
    go.SetCommandPrompt("选择4mm宽闭合矩形轮廓");
    go.GeometryFilter=ObjectType.Curve; go.SubObjectSelect=false;
    go.GetMultiple(1,0); if(go.CommandResult()!=Result.Success)return go.CommandResult();
    int done=0;
    for(int i=0;i<go.ObjectCount;i++) {
     var obj=go.Object(i).Object(); var c=go.Object(i).Curve();
     Polyline poly;
     if(obj==null || obj.IsLocked || obj.IsReference || c==null || !c.IsClosed || !c.TryGetPolyline(out poly) || poly.Count!=5)continue;
     var e=new Vector3d[4]; var u=new Vector3d[4]; var len=new double[4]; bool valid=true;
     for(int j=0;j<4;j++){e[j]=poly[(j+1)%4]-poly[j];len[j]=e[j].Length;u[j]=e[j];if(len[j]<=tol || !u[j].Unitize())valid=false;}
     if(!valid)continue;
     for(int j=0;j<4;j++)if(Math.Abs(u[j]*u[(j+1)%4])>1e-8)valid=false;
     if(!valid || (e[0]+e[2]).Length>tol || (e[1]+e[3]).Length>tol)continue;
     Plane p; double h;
     if(Math.Abs(len[0]-w)<=tol && len[1]>=w-tol){p=new Plane(poly[0],u[0],-u[3]);h=len[1];}
     else if(Math.Abs(len[1]-w)<=tol && len[0]>=w-tol){p=new Plane(poly[1],u[1],-u[0]);h=len[0];}
     else continue;
     for(int j=0;j<4;j++)if(Math.Abs(p.DistanceTo(poly[j]))>tol)valid=false;
     if(!valid || !p.IsValid)continue;
     using(var result=Slot(doc,p,w,h,r)) {
      if(result!=null && doc.Objects.Replace(obj.Id,result))done++;
     }
    }
    doc.Views.Redraw(); RhinoApp.WriteLine("转换完成：{0}；跳过：{1}。支持Undo撤销。",done,go.ObjectCount-done);
    return done>0?Result.Success:Result.Nothing;
   }
  }
  static Result NewSlot(RhinoDoc doc,double s,double w,double r) {
   double mm=10;
   using(var gn=new GetNumber()) {
    gn.SetCommandPrompt("卡口总长度（毫米，至少4）");gn.SetDefaultNumber(10);gn.SetLowerLimit(4,false);
    if(gn.Get()!=GetResult.Number)return gn.CommandResult();mm=gn.Number();
   }
   Point3d center, direction;
   using(var gp=new GetPoint()) {
    gp.SetCommandPrompt("卡口中心");if(gp.Get()!=GetResult.Point)return gp.CommandResult();center=gp.Point();
   }
   var plane=doc.Views.ActiveView.ActiveViewport.ConstructionPlane();
   center=plane.ClosestPoint(center);
   using(var gp=new GetPoint()) {
    gp.SetCommandPrompt("卡口长度方向");gp.SetBasePoint(center,true);gp.DrawLineFromPoint(center,true);
    if(gp.Get()!=GetResult.Point)return gp.CommandResult();direction=gp.Point();
   }
   var y=plane.ClosestPoint(direction)-center;
   if(y.Length<=doc.ModelAbsoluteTolerance || !y.Unitize())return Result.Failure;
   var x=Vector3d.CrossProduct(y,plane.Normal);x.Unitize();
   double h=mm*s;var origin=center-x*(w/2)-y*(h/2);
   using(var c=Slot(doc,new Plane(origin,x,y),w,h,r)) {
    if(c==null)return Result.Failure;
    var id=doc.Objects.AddCurve(c);if(id==Guid.Empty)return Result.Failure;
    doc.Objects.Select(id);doc.Views.Redraw();
   }
   RhinoApp.WriteLine("已创建二维闭合卡口：宽4mm，长{0}mm，四角R1.3mm。",mm);
   return Result.Success;
  }
 }
}
