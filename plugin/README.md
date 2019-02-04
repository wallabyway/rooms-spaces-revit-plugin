# rooms-spaces-revit-plugin
rooms-spaces-revit-plugin

## Description
Revit plugin extracts the data of a room, find its boundray, make direct shape of the room, and attach room number to the shape.

This plugin is based on the other sample [Revit3DRoom](https://github.com/Tamu/Revit3Drooms). Thank you [Tamu](https://github.com/Tamu)! While the codes are modified to add shape with the height of the room (lower floor and upper floor), instead fixed value. In addition, the shape is attached with shared parameters that can be translated by Model Derivative of Forge, instead of the custom data that are not translated.

## Steps of Local Test

1. Install Revit 2019
2. Follow the [tutorial](https://knowledge.autodesk.com/support/revit-products/learn-explore/caas/simplecontent/content/my-first-revit-plug-overview.html) to build the project. 
3. Comment out the line in function **OnStartup** of [RoomExtractor/RoomExtractor.cs](./RoomExtractor/RoomExtractor.cs). This is for local test.
```
    app.ApplicationInitialized += HandleApplicationInitializedEvent;
```
4. Ensure to put one test Revit file under C:\Temp
5. Start Revit. Load the plugin when Revit asks you 
6. Wait a moment, check the folder of the assembly dll. One new Revit file is generated. 
7. Upload the new file to any tool that can view the model in the browser by Forge Viewer. e.g. https://viewer.autodesk.com/ . Check if the rooms are available under **Generic Model**, and if the room number is attached with the shape, as what is shown in the snapshot below:
    <img src="../designautomation/img/result.png" height="400" width="600">

## Steps of Cloud Test
Check [ReadMe](../designautomation/README.md) for details.




