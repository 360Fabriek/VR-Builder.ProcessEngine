# VR-Builder-Core-Runtime
Engine-agnostic core architecture for VR Builder.

## Renaming serialized behaviors and conditions

Process JSON stores CLR type names, not script filenames. Renaming only a `.cs` file does not require migration. When renaming a class or moving its namespace or assembly, keep its old identity on the new class using Unity's `MovedFrom` attribute:

```csharp
using UnityEngine.Scripting.APIUpdating;

[MovedFrom(true, "Old.Namespace", "Old.Assembly", "OldBehavior")]
public class NewBehavior : Behavior<NewBehavior.EntityData>
{
    // Existing implementation.
}
```

Use the old assembly's simple name (without `.dll`). Omitted namespace, assembly, and class arguments default to the current values. The serializer first resolves existing types normally, then checks explicit migrations. Nested types such as `OldBehavior+EntityData` also resolve through the containing class's migration, provided their nested names are unchanged. Generic collections of renamed types are supported. If a nested type itself changes name, give that type its own `MovedFrom` attribute with its old full nested class name, for example `OldBehavior+OldData`.

Unmapped and ambiguous old identities still fail rather than silently loading another behavior or condition. Saving a successfully loaded process writes the current type names. Keep migration attributes while old process files still need to load; this does not infer rename history or repair changed data schemas.

## Additive scenes loaded during a process

Conditions based on `Condition<TData>` retain configuration requests while inactive and apply them immediately before activation. Configuration runs again on each activation, so GUID references can resolve objects from a reloaded scene. Mode changes still configure running conditions immediately. Custom conditions should implement `GetConfigurator()` rather than resolving scene objects directly in an overridden `Configure()` method.

Load required scenes in a blocking activation behaviour before the step that uses their objects. Deferral does not make a condition wait indefinitely for a missing target after activation, and it does not defer behaviour configuration.

`DefaultStepLockHandling` registers inactive objects and initializes the initial locking policy when a scene loads during a process. It then reapplies tracked active-step permissions to objects in that scene without relocking other loaded scenes. Manual lock references are resolved again rather than retaining components across unload/reload. Scene-load handlers detach when the process finishes or becomes inactive.

## Entity cloning

Entities no longer clone themselves. Inject an `IEntityCloner` and replace calls such as `process.Clone()` with `entityCloner.Clone(process)`. Runtime code can use the cloner exposed by `BaseRuntimeConfiguration`; editor integrations should expose a cloner built from their configured `IProcessSerializer`.

Cross-entity references should use `EntityReference<TEntity>`. The cloner discovers and remaps these values automatically, mapping references inside the owned graph to their copies and leaving external references unchanged. `ITransitionData.TargetStep` and `GoToChapterBehavior.EntityData.ChapterGuid` are obsolete compatibility facades; they remain serialized so existing process data still populates the typed references.
