using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using JPMorrow.Revit.Documents;
using System.Collections.Generic;
using System.Linq;
using JPMorrow.BICategories;
using JPMorrow.Revit.Custom.View;
using JPMorrow.Revit.Tools;
using MoreLinq;
using JPMorrow.Revit.Measurements;
using JPMorrow.Tools.Diagnostics;
using System.Windows.Forms;

namespace MainApp
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    [Autodesk.Revit.DB.Macros.AddInId("58F7B2B7-BF6D-4B39-BBF8-13F7D9AAE97E")]
	public partial class ThisApplication : IExternalCommand
	{
        private readonly string FixtureFamilyName = "Conduit Junction Box - Clearance.rfa";
		private string FixtureFamilyNameNoExt { get => FixtureFamilyName.Split('.').First(); }

        public Result Execute(ExternalCommandData cData, ref string message, ElementSet elements)
        {
            string[] dataDirectories = new string[] { "families" };
            bool debugApp = false;

			// set revit documents
			ModelInfo revit_info = ModelInfo.StoreDocuments(cData, dataDirectories, debugApp);
			string fam_path = ModelInfo.GetDataDirectory("families",  true);

			// create view for processing fixtures
			var FixtureCLearanceView = ViewGen.CreateView(revit_info, "FixtureCLearanceGenerated", BICategoryCollection.FixtureClearanceView);

            // collect and filter fixture elements
            FilteredElementCollector coll = new FilteredElementCollector(revit_info.DOC, revit_info.UIDOC.ActiveView.Id);
            var fixtures = coll.OfCategory(BuiltInCategory.OST_ElectricalFixtures).ToElements();
            fixtures = PruneElementsByFamilyName(FixtureFamilyNameNoExt, fixtures, out var failed_family_name_fixtures);

            // promt user about failed fixtures
            if(failed_family_name_fixtures.Any())
            {
                revit_info.SEL.SetElementIds(failed_family_name_fixtures.ToList());
                var result = debugger.show_yesno(
                    header:"Update Jbox Clearances", 
                    err:failed_family_name_fixtures.Count().ToString() + 
                    " fixtures are not of the correct family type. They have been" + 
                    " selected in the model for review.",
                    continue_txt:"Would you like to continue processing the remaining boxes?");

                if(result == DialogResult.Yes) return Result.Succeeded;
            }

            // get top face of each geometry and then generate clearances
            List<FixtureLineGeo> lines = new List<FixtureLineGeo>();
            foreach(var f in fixtures)
            {
                Face top_face = GetTopFaceOfFixture(revit_info.DOC, f, FixtureCLearanceView); 
                if(top_face != null)
                {
                    var l = GetLineGeometryFromFace(top_face).ToArray();
                    lines.Add(new FixtureLineGeo(f, l));
                }
            }

            var failed_generate_fixtures = GenerateFixtureClearances(revit_info, lines, FixtureCLearanceView);

            // promt user about failed fixtures
            if(failed_generate_fixtures.Any())
            {
                revit_info.SEL.SetElementIds(failed_generate_fixtures);
                debugger.show(
                    header:"Update Jbox Clearances", 
                    err:failed_generate_fixtures.Count.ToString() + 
                    " fixtures were not able to have clearances generated" + 
                    " for them. They have been selected in the model for review." );
            }

            return Result.Succeeded;
        }

		/// <summary>
        /// Filters the provided elements based on whether they match the provided family name
        /// </summary>
        /// <param name="family_name">The family name to match against</param>
        /// <param name="elements">elements to filter</param>
        /// <param name="failed_elements">failed element ids that did not pass the filter</param>
        /// <returns>a list of elements that match the provided family name</returns>
		private static IList<Element> PruneElementsByFamilyName(string family_name, IEnumerable<Element> elements, out IEnumerable<ElementId> failed_elements)
		{
            var ret_els = new List<Element>();
            var failed_ids = new List<ElementId>();

			foreach(var f in elements)
			{
				if(f as FamilyInstance != null)
				{
                    var fam_name = (f as FamilyInstance).Symbol.FamilyName;
					if(fam_name.Equals(family_name)) ret_els.Add(f);
					else failed_ids.Add(f.Id);
                }
				else
                    failed_ids.Add(f.Id);
			}

            failed_elements = failed_ids.ToList();
            return ret_els;
        }

        private static Face GetTopFaceOfFixture(Document doc, Element fixture, View3D view)
        {
            Options opt = new Options();
            opt.View = view;
            opt.ComputeReferences = true;
            var geo_el = fixture.get_Geometry(opt);
            Face final_face = null;

            foreach(var geo_obj in geo_el) {
                var geo_inst = geo_obj as GeometryInstance;
                if(geo_inst == null) continue;

                foreach(var obj in geo_inst.GetInstanceGeometry()) {
                    var solid = obj as Solid;
                    if(solid == null || solid.Edges.Size == 0 || solid.Faces.Size == 0) continue;

                    var gstyle = doc.GetElement(solid.GraphicsStyleId) as GraphicsStyle;
                    if(gstyle != null && gstyle.Name.Contains("Light Source")) continue;

                    List<Face> faces = new List<Face>();

                    foreach(Face f in solid.Faces) {
                        if(f == null) continue;
                        faces.Add(f);
                    }

                    if(!faces.Any()) continue;

                    var ordered_faces = faces.OrderByDescending(x => {
                        var pts = new List<XYZ>();

                        foreach(CurveLoop loop in x.GetEdgesAsCurveLoops()) {
                            if(loop == null) continue;
                            foreach(Curve c in loop) {
                                if(c == null) continue;

                                var ll = c as Line;
                                if(ll == null) continue;
                                var pt = RGeo.DerivePointBetween(ll, ll.Length / 2);
                                pts.Add(pt);
                            }
                        }

                        var ret = pts.Any() ? pts.Select(x => x.Z).Average() : -9999;
                        return ret;
                    });

                    if(ordered_faces == null || ordered_faces.Count() == 0) continue;
                    final_face = ordered_faces.First();
                }
            }

            return final_face;
        }

        private static IList<Line> GetLineGeometryFromFace(Face face)
        {
            List<Line> temp_lines = new List<Line>();
            foreach(CurveLoop loop in face.GetEdgesAsCurveLoops()) {
                foreach(Curve c in loop) {
                    var l = c as Line;
                    if(l == null) continue;
                    var ll = Line.CreateBound(face.Project(l.GetEndPoint(0)).XYZPoint, face.Project(l.GetEndPoint(1)).XYZPoint);
                    temp_lines.Add(ll);
                }
            }

            /* if(debugApp) {
                using var debug = new Transaction(revit_info.DOC, "debug symbols");
                debug.Start();

                List<ElementId> sel = new List<ElementId>();
                foreach(var pt in temp_lines.Select(x => RGeo.DerivePointBetween(x, x.Length / 2))) {
                    var fa = MakeDebugPoint(revit_info, pt, debug_sym, null, ws_id);
                    sel.Add(fa.Id);
                }

                debug.Commit();
                revit_info.SEL.SetElementIds(sel);
            } */

            return temp_lines.ToArray();
        }

        private static IList<ElementId> GenerateFixtureClearances(ModelInfo info, IEnumerable<FixtureLineGeo> geos, View3D view)
        {
            var failed_fixtures = new List<ElementId>();

            // TAG FIXTURES
            using TransactionGroup tgrp = new TransactionGroup(info.DOC, "Getting light fixture elevations");
			using Transaction tx = new Transaction(info.DOC, "light fixture elevations");
            tgrp.Start();
			tx.Start();

            double min_len = RMeasure.LengthDbl(info, "2'");

			foreach (var pack in geos) {
				List<double> ray_measurments = new List<double>();

				foreach(var line in pack.Lines) {
					var pt = RGeo.DerivePointBetween(line, line.Length / 2);
					var ray = RevitRaycast.Cast(info, view, BICategoryCollection.FixtureClearanceClash.ToList(), pt, RGeo.PrimitiveDirection.Up);

					if(ray.collisions.Any()) {
						var cols = ray.collisions.OrderBy(x => x.distance).ToList();
						foreach(var coll in cols) {
							if(coll.distance <= min_len) continue;
							ray_measurments.Add(coll.distance);
							break;
						}
					}
				}

				if(!ray_measurments.Any()) continue;

				try 
                {
                    double tolerance = RMeasure.LengthDbl(info, "1\"");

					pack.Fixture.LookupParameter("Height To Structure Min").Set("");
					pack.Fixture.LookupParameter("Height To Structure Max").Set("");

					if(ray_measurments.Count >= 2) 
                    {
						var max = ray_measurments.MaxBy(x => x).First();
						var min = ray_measurments.MinBy(x => x).First();


                        var min_str = RMeasure.LengthFromDbl(info, min);
						var max_str = RMeasure.LengthFromDbl(info, max);

						if(max > min + tolerance) 
                        {
							pack.Fixture.LookupParameter("Height To Structure Min").Set(min_str);
							pack.Fixture.LookupParameter("Height To Structure Max").Set(max_str);
						}
						else 
                        {
							pack.Fixture.LookupParameter("Height To Structure Min").Set(max_str);
						}
					}
					else if(ray_measurments.Count == 1) 
                    {
                        var min_str = RMeasure.LengthFromDbl(info, ray_measurments.First());
						pack.Fixture.LookupParameter("Height To Structure #1").Set(min_str);
					}
					else 
                    {
						// failed
						failed_fixtures.Add(pack.Fixture.Id);
					}
				}
				catch 
                {
					failed_fixtures.Add(pack.Fixture.Id);
				}
            }

			foreach(var id in failed_fixtures) 
            {
				var fixture = info.DOC.GetElement(id);
				fixture.LookupParameter("Height To Structure Min").Set("");
				fixture.LookupParameter("Height To Structure Max").Set("");
			}

			tx.Commit();
			tgrp.Assimilate();

            return failed_fixtures;
        }
	}

    public class FixtureLineGeo {
        public Element Fixture { get; set; } 
        public Line[] Lines { get; set; }

        public FixtureLineGeo(Element fix, Line[] lines) {
            Fixture = fix;
            Lines = lines;
        }
    }
}