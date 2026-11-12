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
  static Curve Slot(RhinoDoc doc, Plane p, double w, double h, double r, bool notched) {
   if(!notched && Math.Abs(w-2*r)<1e-10*w) {
    // Exact capsule: avoid coincident fillets and zero-length end lines.
    if(h<w)h=w;
    if(h==w)return new Circle(new Plane(p.PointAt(r,r),p.XAxis,p.YAxis),r).ToNurbsCurve();
    var capsule=new PolyCurve();
    capsule.Append(new ArcCurve(new Arc(p.PointAt(0,r),p.PointAt(r,0),p.PointAt(w,r))));
    capsule.Append(new LineCurve(p.PointAt(w,r),p.PointAt(w,h-r)));
    capsule.Append(new ArcCurve(new Arc(p.PointAt(w,h-r),p.PointAt(r,h),p.PointAt(0,h-r))));
    capsule.Append(new LineCurve(p.PointAt(0,h-r),p.PointAt(0,r)));
    if(capsule.IsValid && capsule.IsClosed)return capsule;
    capsule.Dispose();return null;
   }
   if(notched) {
    // End notches: radius r=0.5mm, depth r/2=0.25mm.
    // Circle centers lie r-depth outside each end of the slot.
    double d=r/2, a=Math.Sqrt(r*r-(r-d)*(r-d)), m=w/2;
    double k=r*(1-1/Math.Sqrt(2));
    var result=new PolyCurve();
    Action<double,double,double,double> line=(x1,y1,x2,y2)=>result.Append(new LineCurve(p.PointAt(x1,y1),p.PointAt(x2,y2)));
    Action<double,double,double,double,double,double> arc=(x1,y1,xm,ym,x2,y2)=>result.Append(new ArcCurve(new Arc(p.PointAt(x1,y1),p.PointAt(xm,ym),p.PointAt(x2,y2))));
    line(r,0,m-a,0); arc(m-a,0,m,d,m+a,0); line(m+a,0,w-r,0);
    arc(w-r,0,w-k,k,w,r); line(w,r,w,h-r);
    arc(w,h-r,w-k,h-k,w-r,h);
    line(w-r,h,m+a,h); arc(m+a,h,m,h-d,m-a,h); line(m-a,h,r,h);
    arc(r,h,k,h-k,0,h-r); line(0,h-r,0,r); arc(0,r,k,k,r,0);
    if(result.IsValid && result.IsClosed)return result;
    result.Dispose();return null;
   }
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
   bool notched=false;
   if(s<=0 || double.IsNaN(s) || double.IsInfinity(s) || tol<=0 || tol>0.01*s) {
    RhinoApp.WriteLine("请设置标准长度单位，绝对公差不大于0.01mm。"); return Result.Failure;
   }
   using(var spec=new GetString()) {
    spec.SetCommandPrompt("选择板厚（mm）：Q=2 / W=2.5 / E=3 / R=4");
    spec.AddOption("Q");spec.AddOption("W");spec.AddOption("E");spec.AddOption("R");
    spec.AcceptNothing(true);
    while(true) {
     var answer=spec.Get();
     double thickness=4;
     if(answer==GetResult.Option)thickness=new double[]{2,2.5,3,4}[spec.OptionIndex()-1];
     else if(answer==GetResult.String) {
      string value=spec.StringResult().Trim().ToUpperInvariant();
      if(value=="Q" || value=="2")thickness=2;
      else if(value=="W" || value=="2.5")thickness=2.5;
      else if(value=="E" || value=="3")thickness=3;
      else if(value=="R" || value=="4")thickness=4;
      else {RhinoApp.WriteLine("请输入Q/W/E/R或2/2.5/3/4。");continue;}
     } else if(answer!=GetResult.Nothing)return spec.CommandResult();
     w=thickness*s;notched=thickness==2.5;
     r=(notched?0.5:thickness==2?1.0:1.3)*s;break;
    }
   }
   var failed=new System.Collections.Generic.HashSet<Guid>();
   bool completed=false;
   try {
   using(var go=new GetObject()) {
    go.SetCommandPrompt(string.Format("选择要转换的{0}mm矩形轮廓，回车执行",w/s));
    go.GeometryFilter=ObjectType.Curve; go.SubObjectSelect=false;
    go.GetMultiple(1,0); if(go.CommandResult()!=Result.Success)return go.CommandResult();
    int done=0;
    for(int i=0;i<go.ObjectCount;i++)failed.Add(go.Object(i).ObjectId);
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
     using(var result=Slot(doc,p,w,h,r,notched)) {
      if(result!=null && doc.Objects.Replace(obj.Id,result)) {
       done++;failed.Remove(obj.Id);
      }
     }
    }
    completed=true;
    RhinoApp.WriteLine("转换成功：{0}；未成功：{1}。仅未成功对象高亮，Esc清除，支持Undo。",done,failed.Count);
    return Result.Success;
   }
   } finally {
    // Apply result selection only after GetObject has been disposed.
    // Cancel leaves selection handling to Rhino and never scans the document.
    if(completed) {
     doc.Objects.UnselectAll();
     foreach(var id in failed)doc.Objects.Select(id);
     doc.Views.Redraw();
    }
   }
  }
 }
}
