
using System;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using JPMorrow.Tools.Diagnostics;

namespace JPMorrow.Revit.Tools
{
    public static class RGeoDebug
    {
        private static readonly string DebugPtFamilyName = "debug_pt.rfa";
        private static string DebugPtFamilyNameNoExt { get => DebugPtFamilyName.Split('.').First(); }

        public static void DisplayCornerPointsOnElementGeometry(Document doc, Element element, View view)
        {
            Options opt = new Options();
            opt.View = view;
            opt.ComputeReferences = true;
            var geo_el = element.get_Geometry(opt);

            foreach (var geo_obj in geo_el)
            {
                var geo_inst = geo_obj as GeometryInstance;
                if (geo_inst == null) continue;

                foreach (var obj in geo_inst.GetInstanceGeometry())
                {
                    var solid = obj as Solid;
                    DisplayCornerPointsOnSolid(doc, element, solid);
                }
            }
        }

        public static void DisplayCornerPointsOnSolid(Document doc, Element element, Solid solid)
        {
            using TransactionGroup tgrp = new TransactionGroup(doc, "placing debug points");
            using Transaction tx = new Transaction(doc, "placing debug point");
            tgrp.Start();
            tx.Start();

            var bb = RGeo.GetBoundingBoxFromSolid(element, solid);

            if(bb == null) return;

            var pts = new XYZ[] {
                new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z),
                new XYZ(bb.Max.X, bb.Max.Y, bb.Max.Z),
                new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z),
                new XYZ(bb.Min.X, bb.Max.Y, bb.Max.Z),

                new XYZ(bb.Min.X, bb.Min.Y, bb.Max.Z),
                new XYZ(bb.Max.X, bb.Max.Y, bb.Min.Z),
                new XYZ(bb.Max.X, bb.Min.Y, bb.Max.Z),
                new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z),
            };

            var coll = new FilteredElementCollector(doc);

            var sym = coll.OfClass(typeof(FamilySymbol))
                .Where(x => (x as FamilySymbol).FamilyName.Equals(DebugPtFamilyNameNoExt))
                .First() as FamilySymbol;

            if(!sym.IsActive) sym.Activate();

            foreach(var pt in pts)
            {
                FamilyInstance fam = doc.Create.NewFamilyInstance(
					pt, sym, StructuralType.NonStructural);
            }

            tx.Commit();
			tgrp.Assimilate();
        }

        /// EXTERNAL EVENT METHODS
        
        private static DisplayElementGeometryCornerDebugPoints handler_display_element_debug_pts = null;
		private static ExternalEvent exEvent_display_element_debug_pts = null;

        // Sign up the event handlers
        public static void DisplayCornerPointsOnElementGeometrySignUp() 
        {
			handler_display_element_debug_pts = new DisplayElementGeometryCornerDebugPoints();
			exEvent_display_element_debug_pts = ExternalEvent.Create(handler_display_element_debug_pts.Clone() as IExternalEventHandler);
		}

        public static async void ExternDisplayCornerPointsOnElementGeometry(Document doc, Element element, View view)
        {
            await ExternDisplayCornerPointsOnElementGeometryTask(doc, element, view);
        }
        
		private static async Task ExternDisplayCornerPointsOnElementGeometryTask(Document doc, Element element, View view) 
        {
			handler_display_element_debug_pts.Document = doc;
			handler_display_element_debug_pts.Element = element;
            handler_display_element_debug_pts.View = view;
			exEvent_display_element_debug_pts.Raise();

			while(exEvent_display_element_debug_pts.IsPending) 
            {
				await Task.Delay(100);
			}
		}

        private class DisplayElementGeometryCornerDebugPoints : IExternalEventHandler, ICloneable
		{
			public Document Document { get; set; }
            public Element Element { get; set; }
            public View View { get; set; }

			public object Clone() => this;

			public void Execute(UIApplication app)
			{
                try 
                {
                    RGeoDebug.DisplayCornerPointsOnElementGeometry(Document, Element, View);
                }
                catch(Exception ex) 
                {
                    debugger.show(header:"Display Element Geometry Corner Points External Event", err:ex.ToString());
                }
			}

			public string GetName()
			{
				return "Display Element Geometry Corner Points";
			}
		}

        private static DisplaySolidCornerDebugPoints handler_display_solid_debug_pts = null;
		private static ExternalEvent exEvent_display_solid_debug_pts = null;

        // Sign up the event handlers
        public static void DisplaySolidCornerDebugPointsSignUp() 
        {
			handler_display_solid_debug_pts = new DisplaySolidCornerDebugPoints();
			exEvent_display_solid_debug_pts = ExternalEvent.Create(handler_display_solid_debug_pts.Clone() as IExternalEventHandler);
		}

        public static async void ExternDisplaySolidCornerDebugPoints(Document doc, Element element, Solid solid)
        {
            await ExternDisplaySolidCornerDebugPointsTask(doc, element, solid);
        }
        
		private static async Task ExternDisplaySolidCornerDebugPointsTask(Document doc, Element element, Solid solid) 
        {
			handler_display_solid_debug_pts.Document = doc;
			handler_display_solid_debug_pts.Element = element;
            handler_display_solid_debug_pts.Solid = solid;
			exEvent_display_solid_debug_pts.Raise();

			while(exEvent_display_solid_debug_pts.IsPending) 
            {
				await Task.Delay(100);
			}
		}

        private class DisplaySolidCornerDebugPoints : IExternalEventHandler, ICloneable
		{
			public Document Document { get; set; }
            public Element Element { get; set; }
            public Solid Solid { get; set; }

			public object Clone() => this;

			public void Execute(UIApplication app)
			{
                try 
                {
                    RGeoDebug.DisplayCornerPointsOnSolid(Document, Element, Solid);
                }
                catch(Exception ex) 
                {
                    debugger.show(header:"Display Solid Geometry Corner Points External Event", err:ex.ToString());
                }
			}

			public string GetName()
			{
				return "Display Solid Geometry Corner Points";
			}
		}
    }
}