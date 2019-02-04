using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using DesignAutomationFramework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
#region  "delegate events"
        public ExternalDBApplicationResult OnStartup(Autodesk.Revit.ApplicationServices.ControlledApplication app){
            
            //event for design automation
            DesignAutomationBridge.DesignAutomationReadyEvent += HandleDesignAutomationReadyEvent;
            //event for local test
            //app.ApplicationInitialized += HandleApplicationInitializedEvent;

            return ExternalDBApplicationResult.Succeeded;
        }

        //local test entry point
        public void HandleApplicationInitializedEvent(object sender, Autodesk.Revit.DB.Events.ApplicationInitializedEventArgs e){
            try
            { 

                Autodesk.Revit.ApplicationServices.Application app = sender as Autodesk.Revit.ApplicationServices.Application;

                DesignAutomationData data = new DesignAutomationData(app, @"c:\temp\rac_advanced_sample_project.rvt");
                //DesignAutomationData data = new DesignAutomationData(app, @"c:\temp\result.rvt");

                DoJob(data);
            }
            catch(Exception ex)
            {
                throw new InvalidOperationException("Could not ini application!");
            }
        } 
        
        //Design Automation entry point
        public void HandleDesignAutomationReadyEvent(object sender, DesignAutomationReadyEventArgs e){
            e.Succeeded = true;
            DoJob(e.DesignAutomationData);
        }

        public ExternalDBApplicationResult OnShutdown(Autodesk.Revit.ApplicationServices.ControlledApplication app){
            return ExternalDBApplicationResult.Succeeded;
        }
#endregion

        public static void DoJob(DesignAutomationData data){
            if (data == null) throw new ArgumentNullException(nameof(data));

            Application rvtApp = data.RevitApp;
            if (rvtApp == null) throw new InvalidDataException(nameof(rvtApp));

            string modelPath = data.FilePath;
            if (String.IsNullOrWhiteSpace(modelPath)) throw new InvalidDataException(nameof(modelPath));

            Document rvtDoc = data.RevitDoc;
            if (rvtDoc == null) throw new InvalidOperationException("Could not open document!");

            //add shared parameter definition
            AddSetOfSharedParameters(rvtDoc);  

            try
            {

#region "delete existing direct shapes"
                // Deleting existing DirectShape 
                // might need to filter out the shapes for room only? 
                // get ready to filter across just the elements visible in a view 
                FilteredElementCollector coll = new FilteredElementCollector(rvtDoc );
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
#endregion
                //get all rooms
                FilteredElementCollector m_Collector = new FilteredElementCollector(rvtDoc);
                m_Collector.OfCategory(BuiltInCategory.OST_Rooms);
                IList<Element> m_Rooms = m_Collector.ToElements();
                int roomNbre = 0;

                ElementId roomId = null;

                //  Iterate the list and gather a list of boundaries
                foreach (Room room in m_Rooms)
                {

                    //  Avoid unplaced rooms
                    if (room.Area > 1)
                    { 
#region "found box of a room"
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
#endregion
#region "add direct shape"
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
                                SketchPlane m_SketchPlane = SketchPlane.Create(rvtDoc, m_Plane); // 2014

                                //height of room
                                Location loc = room.Location; 
                                LocationPoint lp = loc as LocationPoint; 
                                Level oBelow = getNearestBelowLevel(rvtDoc, lp.Point.Z);
                                Level oUpper = getNearestUpperLevel(rvtDoc, lp.Point.Z); 
                                double height = oUpper.Elevation - oBelow.Elevation;

                                Solid roomSolid;
                                roomSolid = GeometryCreationUtilities.CreateExtrusionGeometry(curveLoopList, ptNormal, height);

                                DirectShape ds = DirectShape.CreateElement(rvtDoc, new ElementId(BuiltInCategory.OST_GenericModel));

                                ds.SetShape(new GeometryObject[] { roomSolid });

                                //make a note with room number
                                roomId = ds.Id; 

                                roomNbre += 1;
                                tr.Commit(); 

                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e.Message);

                            }
#endregion


                        }

                        //add room number as shared parameters to the objects
#region "add shared parameter values"
                        using (Transaction tx = new Transaction(rvtDoc))
                        {
                            tx.Start("Change P");

                            Element readyDS = rvtDoc.GetElement(roomId);
                            Parameter p = readyDS.LookupParameter("RoomNumber");
                            if (p != null)
                            {
                                p.Set(room.Number.ToString());
                            }
                            tx.Commit();
                        }
                        Debug.Write("room id:" + roomId.IntegerValue.ToString());
#endregion

                    }
                } 


            }
            catch (Exception ex)
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

        //The funtions below are referred from Revit SDK Sample
        // ReadonlySharedParameters
#region "define shared parameters"
        private static String GetRandomSharedParameterFileName()
        {

            String randomFileName = System.IO.Path.GetRandomFileName();
            String fileRoot = Path.GetFileNameWithoutExtension(randomFileName);
            String spFile = Path.ChangeExtension(randomFileName, "txt"); 
            String filePath = Path.Combine(System.IO.Directory.GetCurrentDirectory(), spFile);
            StreamWriter writer = File.CreateText(filePath);
            writer.Close();
            return filePath;
        }

        public static void AddSetOfSharedParameters(Document doc)
        {
            Application app = doc.Application;

            String filePath = GetRandomSharedParameterFileName();

            app.SharedParametersFilename = filePath;

            DefinitionFile dFile = app.OpenSharedParameterFile();
            DefinitionGroup dGroup = dFile.Groups.Create("Demo group");
            List<SharedParameterBindingManager> managers = BuildSharedParametersToCreate();
            using (Transaction t = new Transaction(doc, "Bind parameters"))
            {
                t.Start();
                foreach (SharedParameterBindingManager manager in managers)
                {
                    manager.Definition = dGroup.Definitions.Create(manager.GetCreationOptions());
                    manager.AddBindings(doc);
                }
                t.Commit();
            }
        }

        private static List<SharedParameterBindingManager> BuildSharedParametersToCreate()
        {
            List<SharedParameterBindingManager> sharedParametersToCreate =
                new List<SharedParameterBindingManager>();

            SharedParameterBindingManager manager = new SharedParameterBindingManager();
            manager.Name = "RoomNumber";
            manager.Type = ParameterType.Text;
            manager.UserModifiable = false;
            manager.Description = "A read-only instance parameter used for coordination with external content.";
            manager.Instance = true; 
            manager.AddCategory(BuiltInCategory.OST_GenericModel); 
            manager.ParameterGroup = BuiltInParameterGroup.PG_IDENTITY_DATA;
            manager.UserVisible = true;
            sharedParametersToCreate.Add(manager);   // Look up syntax for this automatic initialization.  
            return sharedParametersToCreate;
        }
#endregion  

    }
}
