LODRoads v1.0.1 DEMO, Export and Hub not included

=================LODRoads spline and hub controls==================
Left mouse button - add node
Left mouse button on node - select node, open foldout in inspector
Left mouse button on blue node - select another spline/hub
Shift+Left mouse button on node - delete node

Spline controls
NodeTangent
Tangents of selected node with auto tangents disabled can be edited in scene

Link
Selection of any two nodes will create link between them

Segment
Selecting non-terminal node will split spline in two in the node
Selecting non-terminal nodes of two different splines will join them
Hierarchy > 3D Object > LODRoad - create spline object

Hub controls
Selecting node creates handle to rotate the node in scene
Selecting nodes of two different hubs will create spline between them
Hierarchy > 3D Object > LODCrossRoad - create hub object

=========================Terrain delta============================
Terrain modification are not stored in undo buffer. Instead, they are stored in internal delta textures.
This allows to revert LODRoad terrain changes any time while keeping all other changes. Use 'Unpaint terrain' in Spline and Hub menus to revert changes.
Changes are automatically reverted when any Spline or Hub is disabled. 
This happens when: scene built, scene closed, scene recompiled, Spline or Hub Gameobject deleted, Spline or Hub deleted or disabled.
Use 'Apply terrain changes' in Spline and Hub menus to prevent revert of changes. This can be reverted by Editor undo.
Terrain is automatically painted When Spline or Hub start, that is, on scene load, Spline or Hub created, Spline or Hub delete undo.

===========================Export===============================
Selecting GameObjects in Hierarchy with Spline or Hub scripts  will enable these menus:
GameObject > LODRoad > Export prefab and mesh - save selection as prefab asset and dependent meshes as asset
GameObject > LODRoad > Export mesh - save selection as mesh
GameObject > LODRoad > Bake - merge selection into one mesh per each LOD, create prefab, place in scene on position of selection, disable selected GameObjects
GameObject > LODRoad > Bake and replace - same as bake, but selection is deleted

========================LODRoads code==========================
Hub and Spline runs Update in edit mode.
Hub and Spline code is disabled in build. Define LODROAD_INCLUDE_IN_BUILD in each file to enable in build.
Look into SplineEditor and HubEditor on how to use Spline, Hub, Exporter and TerrainDelta APIs.
Undefining LODCROSSROAD in Spline and TerrainDelta will remove dependency on Hub.