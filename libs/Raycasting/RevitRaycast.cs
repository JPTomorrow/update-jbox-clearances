/*
    Author: Justin Morrow
    Date Created: 5/13/2021
    Description: A Module for performing rudamentary raycasting in Revit
*/

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using JPMorrow.Tools.Diagnostics;

namespace JPMorrow.Revit.Tools
{
    public class RRay
    {
        public XYZ Direction { get; private set; } = new XYZ(-9999,-9999,-9999);
        public XYZ StartPoint { get; private set; } = new XYZ(-9999,-9999,-9999);

        private List<RRayCollision> _collisions { get; set; } = new List<RRayCollision>();
        public IList<RRayCollision> Collisions { get => _collisions; }
        public bool HasCollisions { get => _collisions.Any(); }

		public RRay(IEnumerable<RRayCollision> collisions, XYZ direction, XYZ start_point)
		{
            Direction = direction;
            StartPoint = start_point;
            _collisions = collisions.ToList();
        }

        public bool GetFarthestCollision(out RRayCollision col)
		{
			if(!HasCollisions)
			{
                col = null;
                return false;
			}
			var max = _collisions.Max(y => y.Distance);
			col = _collisions.Where(x => x.Distance == max).First();
			return true;
		}

		public bool GetNearestCollision(out RRayCollision col)
		{
			if(!HasCollisions)
			{
                col = null;
                return false;
			}
			var min = _collisions.Min(y => y.Distance);
			col = _collisions.Where(x => x.Distance == min).First();
			return true;
		}
	}

	public class RRayCollision
	{
        public XYZ Point { get; private set; } = new XYZ(-9999,-9999,-9999);
        public double Distance { get; private set; } = -9999;
        public ElementId OtherId { get; private set; } = ElementId.InvalidElementId;

		public bool IsValid { get => OtherId != ElementId.InvalidElementId; }

        public RRayCollision(XYZ point, double distance, ElementId other_id)
		{
            Point = point;
            Distance = distance;
            OtherId = other_id;
        }
    }

	public static class RevitRaycast
	{
		/// <summary>
		/// Cast a ray in a direction and return all collisions
		/// </summary>
		public static RRay Cast(
			Document doc, View3D view, List<BuiltInCategory> find_cats, 
			XYZ origin_pt, XYZ ray_dir, double max_distance = -1, 
			List<ElementId> ids_to_ignore = null)
		{
			

			bool prune_lengths = max_distance >= 0;

			ReferenceIntersector ref_intersect = new ReferenceIntersector(
				new ElementMulticategoryFilter(find_cats), FindReferenceTarget.Element, view)
			{
				FindReferencesInRevitLinks = true
			};

			List<ReferenceWithContext> rwcs = new List<ReferenceWithContext>();
			rwcs = ref_intersect.Find(origin_pt, ray_dir).ToList();

			if(prune_lengths)
			{
				foreach(var rwc in rwcs.ToArray())
				{
					if(rwc.Proximity > max_distance)
						rwcs.Remove(rwc);
				}
			}

			List<RRayCollision> temp_collisions_storage = new List<RRayCollision>();
			if(ids_to_ignore == null)
				ids_to_ignore =  new List<ElementId>();

			foreach(var rwc in rwcs)
			{
				Reference r = rwc.GetReference();
				if(ids_to_ignore.Any(x => x.IntegerValue == r.ElementId.IntegerValue)) continue;

				Element collided_element = doc.GetElement(r.ElementId);
				if(collided_element == null) continue;

                RRayCollision ray_collision;
                if(max_distance == -1)
				{
                    ray_collision = new RRayCollision(r.GlobalPoint, rwc.Proximity, collided_element.Id);
					temp_collisions_storage.Add(ray_collision);
				}
				else
				{
					if(rwc.Proximity <= max_distance)
					{
						ray_collision = new RRayCollision(r.GlobalPoint, rwc.Proximity, collided_element.Id);
						temp_collisions_storage.Add(ray_collision);
					}
				}
			}

            RRay ray = new RRay(temp_collisions_storage, ray_dir, origin_pt);
			return ray;
		}

		public static IEnumerable<ElementId> CastSphere(
			Document doc, XYZ start_pt, double radius, 
			BuiltInCategory bic = BuiltInCategory.INVALID)
		{
			Solid CreateSphereAt(XYZ center, double radius)
			{
				Frame frame = new Frame( center,
				XYZ.BasisX, XYZ.BasisY, XYZ.BasisZ );

				// Create a vertical half-circle loop;
				// this must be in the frame location.

				Arc arc = Arc.Create(
				center - radius * XYZ.BasisZ,
				center + radius * XYZ.BasisZ,
				center + radius * XYZ.BasisX );

				Line line = Line.CreateBound(
				arc.GetEndPoint(1),
				arc.GetEndPoint(0) );

				CurveLoop halfCircle = new CurveLoop();
				halfCircle.Append( arc );
				halfCircle.Append( line );

				List<CurveLoop> loops = new List<CurveLoop>( 1 );
				loops.Add( halfCircle );

				return GeometryCreationUtilities
				.CreateRevolvedGeometry(
					frame, loops, 0, 2 * Math.PI );
			}

			Solid sphere = CreateSphereAt(start_pt, radius);

			ElementIntersectsSolidFilter intersectSphere = new ElementIntersectsSolidFilter(sphere);

			FilteredElementCollector coll = new FilteredElementCollector(doc);


			var intersection = bic == BuiltInCategory.INVALID ? coll.WherePasses(intersectSphere).ToElementIds() : coll.OfCategory(bic).WherePasses(intersectSphere).ToElementIds();

			return intersection;
		}

		/// <summary>
		/// Create model lines in a 3D view
		/// </summary>
		public static void DrawModelLines(
			Document doc, ExternalEvent ev, InsertModelLine iml,  
			WorksetId workset_id, XYZ[] pts = null)
		{
			if(pts == null || !pts.Any())
				throw new Exception("No points were provided for drawing.");

			if(pts.Count() % 2 != 0)
				throw new Exception("Odd number of points feed to the display model lines.");

			iml.Document = doc;
			iml.Line_Points = pts;
			iml.Workset_Id = workset_id;
			ev.Raise();
		}
	}

	public class InsertModelLine : IExternalEventHandler
	{
		public Document Document { get; set; }
		public XYZ[] Line_Points { get; set; }
		public WorksetId Workset_Id { get; set; }

		public void Execute(UIApplication app)
		{
			using (Transaction tx = new Transaction(Document, "placing model line"))
			{
				tx.Start();
				List<XYZ> pts_queue = new List<XYZ>(Line_Points);
				while(pts_queue.Count > 0)
				{
					XYZ[] current_pts = pts_queue.Take(2).ToArray();
					foreach(var pt in current_pts)
						pts_queue.Remove(pt);

					string line_str_style = "<Hidden>"; // system linestyle guaranteed to exist
					Create3DModelLine(current_pts[0], current_pts[1], line_str_style, Workset_Id);
				}
				tx.Commit();
			}
		}

		public SketchPlane NewSketchPlanePassLine(Line line)
		{
			XYZ p = line.GetEndPoint(0);
			XYZ q = line.GetEndPoint(1);
			XYZ norm = new XYZ(-10000, -10000, 0);
			Plane plane = Plane.CreateByThreePoints(p, q, norm);
			SketchPlane skPlane = SketchPlane.Create(Document, plane);
			return skPlane;
		}

		public void Create3DModelLine(XYZ p, XYZ q, string line_style, WorksetId id)
		{
			try
			{
				if (p.IsAlmostEqualTo(q))
				{
					debugger.show(err: "Expected two different points.");
					return;
				}
				Line line = Line.CreateBound(p, q);
				if (null == line)
				{
					debugger.show(err: "Geometry line creation failed.");
					return;
				}

				ModelCurve model_line_curve = null;
				model_line_curve = Document.Create.NewModelCurve(line, NewSketchPlanePassLine(line));

				Parameter workset_param = model_line_curve.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM );
				workset_param.Set(Workset_Id.IntegerValue);

				// set linestyle
				ICollection<ElementId> styles = model_line_curve.GetLineStyleIds();
				foreach(ElementId eid in styles)
				{
					Element e = Document.GetElement(eid);
					if (e.Name == line_style)
					{
						model_line_curve.LineStyle = e;
						break;
					}
				}
			}
			catch (Exception ex)
			{
				debugger.show(err:ex.ToString());
			}
		}

		public string GetName()
		{
			return "Insert Model Line";
		}
	}
}