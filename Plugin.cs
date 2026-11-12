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
   using(var spec=new GetOption()) {
    spec.SetCommandPrompt("选择板材规格：四毫米或二点五毫米");
    spec.AddOption(new Rhino.UI.LocalizeStringPair("Four", "四毫米"));
    int thin=spec.AddOption(new Rhino.UI.LocalizeStringPair("Thin", "二点五毫米"));
    if(spec.Get()!=GetResult.Option)return spec.CommandResult();
    notched=spec.OptionIndex()==thin;
    if(notched){w=2.5*s;r=0.5*s;}
   }
   using(var opt=new GetOption()) {
    opt.SetCommandPrompt("二维卡口：选择操作");
    int n=opt.AddOption(new Rhino.UI.LocalizeStringPair("New", "新建"));
    opt.AddOption(new Rhino.UI.LocalizeStringPair("Convert", "转换"));
    if(opt.Get()!=GetResult.Option)return opt.CommandResult();
    if(opt.OptionIndex()==n) return NewSlot(doc,s,w,r,notched);
   }
   using(var go=new GetObject()) {
    go.SetCommandPrompt(string.Format("选择{0}mm宽闭合矩形轮廓",w/s));
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
     using(var result=Slot(doc,p,w,h,r,notched)) {
      if(result!=null && doc.Objects.Replace(obj.Id,result))done++;
     }
    }
    doc.Views.Redraw(); RhinoApp.WriteLine("转换完成：{0}；跳过：{1}。支持Undo撤销。",done,go.ObjectCount-done);
    return done>0?Result.Success:Result.Nothing;
   }
  }
  static Result NewSlot(RhinoDoc doc,double s,double w,double r,bool notched) {
   double mm=10;
   using(var gn=new GetNumber()) {
    gn.SetCommandPrompt(string.Format("卡口总长度（毫米，至少{0}）",w/s));gn.SetDefaultNumber(10);gn.SetLowerLimit(w/s,false);
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
   using(var c=Slot(doc,new Plane(origin,x,y),w,h,r,notched)) {
    if(c==null)return Result.Failure;
    var id=doc.Objects.AddCurve(c);if(id==Guid.Empty)return Result.Failure;
    doc.Objects.Select(id);doc.Views.Redraw();
   }
   RhinoApp.WriteLine("已创建二维闭合卡口：宽{0}mm，长{1}mm，四角R{2}mm。",w/s,mm,r/s);
   if(notched)RhinoApp.WriteLine("两端内凹圆弧：R0.5mm，深0.25mm，已合并为闭合轮廓。");
   return Result.Success;
  }
 }
}


