using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using DesignAutomationFramework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RoomExtractor
{
    [Autodesk.Revit.Attributes.Regeneration(Autodesk.Revit.Attributes.RegenerationOption.Manual)]
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class RoomExtractor: IExternalDBApplication
    { 
        public ExternalDBApplicationResult OnStartup(Autodesk.Revit.ApplicationServices.ControlledApplication app){
            DesignAutomationBridge.DesignAutomationReadyEvent += HandleDesignAutomationReadyEvent;
            //app.ApplicationInitialized += HandleApplicationInitializedEvent;

            return ExternalDBApplicationResult.Succeeded;
        }

        //local test entry point
        public void HandleApplicationInitializedEvent(object sender, Autodesk.Revit.DB.Events.ApplicationInitializedEventArgs e){
            try
            {
                Autodesk.Revit.ApplicationServices.Application app = sender as Autodesk.Revit.ApplicationServices.Application;
                DesignAutomationData data = new DesignAutomationData(app, @"c:\rac_advanced_sample_project.rvt");
                DoJob(data);
            }
            catch(Exception ex)
            {
                throw new InvalidOperationException("Could not ini application!"); 
            }
        } 
        public ExternalDBApplicationResult OnShutdown(Autodesk.Revit.ApplicationServices.ControlledApplication app){
            return ExternalDBApplicationResult.Succeeded;
        }

        //Design Automation entry point
        public void HandleDesignAutomationReadyEvent(object sender, DesignAutomationReadyEventArgs e){
            e.Succeeded = true;
            DoJob(e.DesignAutomationData);
        }

        public static void DoJob(DesignAutomationData data){
            if (data == null) throw new ArgumentNullException(nameof(data));

            Application rvtApp = data.RevitApp;
            if (rvtApp == null) throw new InvalidDataException(nameof(rvtApp));

            string modelPath = data.FilePath;
            if (String.IsNullOrWhiteSpace(modelPath)) throw new InvalidDataException(nameof(modelPath));

            Document rvtDoc = data.RevitDoc;
            if (rvtDoc == null) throw new InvalidOperationException("Could not open document.");

            Autodesk.Revit.DB.View view;
            view = rvtDoc.ActiveView;

            try
            {

                // Deleting existing DirectShape 
                // might need to filter out the shapes for room only? 
                // get ready to filter across just the elements visible in a view 
                FilteredElementCollector coll = new FilteredElementCollector(rvtDoc);
                coll.OfClass(typeof(DirectShape));
                IEnumerable<DirectShape> DSdelete = coll.Cast<DirectShape>();

                using (Transaction tx = new Transaction(rvtDoc))
                {
                    tx.Start("Delete Direct Shape");

                    try
                    {
                        foreach (DirectShape ds in DSdelete)
                        {
                            ICollection<ElementId> ids = rvtDoc.Delete(ds.Id);
                        }

                        tx.Commit();
                    }
                    catch (ArgumentException)
                    {
                        tx.RollBack();
                        throw new InvalidOperationException("Delete Direct Shape Failed!");
                    }
                }

                //get all rooms
                FilteredElementCollector m_Collector = new FilteredElementCollector(rvtDoc);
                m_Collector.OfCategory(BuiltInCategory.OST_Rooms);
                IList<Element> m_Rooms = m_Collector.ToElements();
                int roomNbre = 0;

                //  Iterate the list and gather a list of boundaries
                foreach (Room room in m_Rooms)
                {

                    //  Avoid unplaced rooms
                    if (room.Area > 1)
                    { 
                        String _family_name = "testRoom-" + room.UniqueId.ToString();

                        using (Transaction tr = new Transaction(rvtDoc))
                        {
                            tr.Start("Create Mass");

                            // Found BBOX

                            BoundingBoxXYZ bb = room.get_BoundingBox(null);
                            XYZ pt = new XYZ((bb.Min.X + bb.Max.X) / 2, (bb.Min.Y + bb.Max.Y) / 2, bb.Min.Z);


                            //  Get the room boundary
                            IList<IList<BoundarySegment>> boundaries = room.GetBoundarySegments(new SpatialElementBoundaryOptions()); // 2012


                            // a room may have a null boundary property:
                            int n = 0;

                            if (null != boundaries)
                            {
                                n = boundaries.Count;
                            }


                            //  The array of boundary curves
                            CurveArray m_CurveArray = new CurveArray();
                            //  Iterate to gather the curve objects


                            TessellatedShapeBuilder builder = new TessellatedShapeBuilder();
                            builder.OpenConnectedFaceSet(true);

                            // Add Direct Shape
                            List<CurveLoop> curveLoopList = new List<CurveLoop>();

                            if (0 < n)
                            {
                                int iBoundary = 0, iSegment;

                                foreach (IList<BoundarySegment> b in boundaries) // 2012
                                {
                                    List<Curve> profile = new List<Curve>();
                                    ++iBoundary;
                                    iSegment = 0;

                                    foreach (BoundarySegment s in b)
                                    {
                                        ++iSegment;
                                        Curve curve = s.GetCurve(); // 2016 
                                        profile.Add(curve); //add shape for instant object

                                    }


                                    try
                                    {
                                        CurveLoop curveLoop = CurveLoop.Create(profile);
                                        curveLoopList.Add(curveLoop);
                                    }
                                    catch (Exception ex)
                                    {
                                        //Debug.WriteLine(ex.Message);
                                    }

                                }
                            } 

                            try
                            {

                                SolidOptions options = new SolidOptions(ElementId.InvalidElementId, ElementId.InvalidElementId);

                                Frame frame = new Frame(pt, XYZ.BasisX, -XYZ.BasisZ, XYZ.BasisY);

                                //  Simple insertion point
                                XYZ pt1 = new XYZ(0, 0, 0);
                                //  Our normal point that points the extrusion directly up
                                XYZ ptNormal = new XYZ(0, 0, 100);
                                //  The plane to extrude the mass from
                                Plane m_Plane = Plane.CreateByNormalAndOrigin(ptNormal, pt1);
                                // SketchPlane m_SketchPlane = m_FamDoc.FamilyCreate.NewSketchPlane(m_Plane);
                                SketchPlane m_SketchPlane = SketchPlane.Create(rvtDoc, m_Plane); // 2014

                                Location loc = room.Location;

                                LocationPoint lp = loc as LocationPoint;

                                Level oBelow = getNearestBelowLevel(rvtDoc, lp.Point.Z);
                                Level oUpper = getNearestUpperLevel(rvtDoc, lp.Point.Z);

                                double height = oUpper.Elevation - oBelow.Elevation;

                                Solid roomSolid;

                                // Solid roomSolid = GeometryCreationUtilities.CreateRevolvedGeometry(frame, new CurveLoop[] { curveLoop }, 0, 2 * Math.PI, options);
                                roomSolid = GeometryCreationUtilities.CreateExtrusionGeometry(curveLoopList, ptNormal, height);

                                DirectShape ds = DirectShape.CreateElement(rvtDoc, new ElementId(BuiltInCategory.OST_GenericModel));

                                ds.SetShape(new GeometryObject[] { roomSolid }); 

                                roomNbre += 1;



                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e.Message);

                            }
                            tr.Commit();

                        }

                    }
                }


            }
            catch(Exception ex)
            {
                throw new InvalidOperationException(ex.ToString());
            }


            ModelPath path = ModelPathUtils.ConvertUserVisiblePathToModelPath("result.rvt");
            rvtDoc.SaveAs(path, new SaveAsOptions()); 



        }

        //get Nearest Below Level, given a Z
        private static Level getNearestBelowLevel(Document doc, double _Zvaue)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().Where(x => x.Elevation <= _Zvaue).OrderByDescending(x => x.Elevation).FirstOrDefault();
        }

        //get Nearest Upper Level, given a Z 
        private static Level getNearestUpperLevel(Document doc, double _Zvaue)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().Where(x => x.Elevation > _Zvaue).OrderBy(x => x.Elevation).FirstOrDefault();
        }


    }
}
