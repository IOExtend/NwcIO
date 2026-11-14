---
title: Scene adapter
---

# NwcIO.Scene: Navisworks to IOScene

`NwcIO.Scene` converts a Navisworks model file (`.nwc` written by a Navisworks exporter) into an `IOScene` `SceneDocument` for viewers and coordination tools. The scene follows the [IOScene contract](https://github.com/IOExtend/IOScene/blob/main/docs/contract.md) and its [key rules](https://github.com/IOExtend/IOScene/blob/main/docs/keys.md): `SceneModel.Key` plus `SceneElement.SourceId` is the identity that may be stored outside the scene, and the numeric ids are local to it.

**Status:** preview. `NwcIO.Scene` is released on the `NwcIO` release tag with exactly the same version as `NwcIO`, and needs `IOScene` 0.1.0 or a later 0.1 version.

## Usage

```csharp
using IOScene;
using NwcIO.Scene;

NwcSceneResult result = NwcSceneReader.Read("Tower.nwc");

foreach (SceneDiagnostic d in result.Diagnostics)
{
    Console.WriteLine($"{d.Severity} {d.Code}: {d.Message}");
}

SceneJson.Save(result.Document, "Tower.scene.json");
```

`NwcSceneReader.Read(NwcDocument, NwcSceneOptions)` converts a document that is already open. It needs `NwcSceneOptions.ModelKey`, because a document has no file name, and a document opened from a model file with the default `NwcReaderOptions`. It throws `ArgumentException` for a document without a scene tree. `NwcSceneOptions.LengthUnit` replaces the unit of the document (see [Limits](#limits)).

## What the scene holds

The scene has one model. Geometry and instance positions are in metres.

| Scene part | Content |
| --- | --- |
| `SceneModel.Key`, `Name` | `NwcSceneOptions.ModelKey`, else the file name with its extension. |
| `SceneModel` fields | `SourceFormat` is `nwc`; `Path` is set only when the scene is read from a path; `CreatedAt` is the source timestamp as `yyyy-MM-ddTHH:mm:ssZ`; `Application` is the loader plugin after its last `:` (for example `lcldrevit`); `SourceLengthUnit` is `ft` when the unit used is feet. |
| Elements | One `SceneElement` per tree node, `Id` = the node's preorder index with the root as 0, `Name` = the node name. Kind: the root is `Model`; a depth-1 node holding a Revit level is `Level`; a node that is a Revit element, or the second node of a sibling pair, is `Element`; a leaf outside elements (no children, tag other than 53) is `Element` too; every other node is `Group`. |
| Sibling pair | A Revit export may write one element twice under one parent. The first in stored order is the element; the second maps as a part: its `ParentId` is the first, and it has no `SourceId`. The same element under different parents (a linked model) is not supported. |
| `SourceId` | The element's export GUID (`NwcNode.ElementGuid`) in format `D`, on element nodes only. This derivation does not change; a golden test pins it. |
| `IfcGuid` | The element's IFC GlobalId (`NwcNode.IfcGlobalId`), on element nodes only. This derivation does not change; a golden test pins it. |
| `Category`, `TypeName` | Revit files only, on `Element`-kind nodes only, by tree position: the category is the name at depth 2 on the path from the root, the type name the name at depth 4 when the element is at depth 5; otherwise empty. This derivation does not change; a golden test pins it. |
| Levels | Revit files only. A depth-1 node whose `LcRevitId` tab holds the element id of a level (an `OST_Levels` reference tab) becomes a `SceneLevel` named like the node, and the node and all its descendants carry its `LevelId`. `Elevation` is not written. This derivation does not change; a golden test pins it. |
| Property sets | One `ScenePropertySet` per property tab object, listed once for every element that refers to the tab (also when one element refers to it twice). A tab whose properties are all left out has no set. |
| Keys | Set key `nwc:<tab internal name>` for the tabs `LcRevitData_Element`, `LcRevitData_Type`, `LcRevitMaterialProperties` and `LcRevitId`; property key `<set key>.<property internal name>` for the Revit element properties (category, category id, family, element id, name, type, `LcRevitId`) and for the names `lcldrevit_parameter_-<n>` and `revit_<name>`, except `revit_mat_cust_prop_*`. Other tabs and properties have no key. This derivation does not change; a golden test pins it. |
| Geometry, instances | Shared geometry is written once per geometry record and instanced by each mesh with its transform (translation in metres). Triangles carry the stored normals, with a zero normal replaced by the area-weighted average of its triangles. Line meshes become `Lines` geometries (see [Limits](#limits)). |
| Materials | Decoded from the file on the strict path: name, colour and alpha, one `SceneMaterial` per distinct name and colour, in order of first use. `Roughness` and `Metalness` keep IOScene's defaults. |
| Grids | A Revit grid (category `OST_Grids`) with exactly one single-segment line mesh that is horizontal within 1e-6 m becomes a line of one grid system named `Grids`, with `Elevation` = the start Z. Other grids stay elements. |

### Property text

A value is written as text under its key by the stored value type:

| Value type | Text |
| --- | --- |
| 1 (number) | The number, invariant culture, six significant digits. |
| 2, 4, 8, 9 (integer, string, other text types) | The stored text unchanged. |
| 3 (boolean) | `0` for a stored 0, otherwise `1`. |
| 6 (length) | Feet converted to millimetres, six significant digits, suffix ` mm`. |
| 10 (area) | Square feet converted to square metres, suffix ` m²`. |
| 11 (volume) | Cubic feet converted to cubic metres, suffix ` m³`. |
| 7 (angle) | Radians converted to degrees (`180 / π`), suffix ` °`. |

A property with no text, a non-finite number, numeric text that does not parse, or an unknown value type is left out. Lengths, areas, volumes and angles are left out too while the unit is not feet. The text a value is written with under a key is frozen with the key: changing it is a breaking release.

## Diagnostics

`NwcSceneResult.Diagnostics` lists what the conversion could not carry over.

| Code | Severity | Meaning |
| --- | --- | --- |
| `no-source-id` | Info | Leaf nodes outside any Revit element have no `SourceId` (one aggregated entry with the count). |
| `empty-mesh` | Info | A mesh has no triangles and no line segments; the entry names the element's GUID. |
| `unit-unknown` | Warning | The length unit is unknown (the file does not name it and `LengthUnit` is not set, or `LengthUnit` is `Unknown`): geometry, grid lines and length, area, volume and angle values are left out. |
| `line-colour-mixed` | Warning | Per geometry record: line segments join vertices of different colours and are drawn with the material colour. |
| `line-colours-capped` | Warning | Per geometry record: the record has more than 64 line colours; segments of the further colours are drawn with the material colour. |
| `viewpoints-skipped` | Warning | The document has saved viewpoints, or a saved-views entry; they are not written. |
| `unplaced-mesh` | Warning | Meshes without a node in the scene tree (for example, the file was not read through its DbCache directory) are left out. |
| `properties-outside-tabs` | Warning | Nodes carry properties that no property tab holds (same cause); they are left out. |

## Limits

- `.nwd` partition trees and linked Revit models are not supported: `Read` throws `NotSupportedException`.
- Level elevations are not written, and saved viewpoints are not written (`viewpoints-skipped`): Navisworks stores no stable viewpoint id.
- Files from a non-Revit loader name no unit: geometry, grid lines and dimensioned values are left out with a `unit-unknown` warning, and there are no categories or levels. Set `NwcSceneOptions.LengthUnit` to bring the geometry back in metres; the option always wins over the unit of the document.
- A document not read through the file's DbCache directory has no geometry and no properties in the scene; both are reported (`unplaced-mesh`, `properties-outside-tabs`).
- A document opened with `ReadPropertyRecords`, `ReadMeshes` or `ReadMaterials` off lacks those parts, and nothing says so: the document does not record its options.
- A line geometry carries only the vertices its own segments use, grouped by segment colour, one geometry per colour. Segments joining two colours go to one more group drawn with the material colour, and each geometry record with such segments adds one `line-colour-mixed` warning. A geometry record keeps at most 64 colour groups, because every mesh of the record places one instance per group: segments of further colours join the material-colour group, and the record adds one `line-colours-capped` warning.
- Materials carry IOScene's default `Roughness` and `Metalness`, as `RvtIO.Scene` does: the file's shininess is not mapped, so the same element looks the same from a `.rvt` and a `.nwc`.
