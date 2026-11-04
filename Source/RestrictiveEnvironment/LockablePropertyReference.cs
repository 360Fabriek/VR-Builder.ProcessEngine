// Copyright (c) 2013-2019 Innoactive GmbH
// Licensed under the Apache License, Version 2.0
// Modifications copyright (c) 2021-2026 MindPort GmbH

using System.Runtime.Serialization;
using VRBuilder.Core.Properties;
using VRBuilder.Core.SceneObjects;
using VRBuilder.Core.Utils;

namespace VRBuilder.Core.RestrictiveEnvironment
{
    /// <summary>
    /// Serializable reference to a <see cref="LockableProperty"/>
    /// </summary>
    [DataContract(IsReference = true)]
    public class LockablePropertyReference
    {
        /// <summary>
        /// Reference to the scene object the LockableProperty is attached to.
        /// </summary>
        [DataMember]
        public SingleSceneObjectReference TargetObject;

        /// <summary>
        /// Type name of the LockableProperty.
        /// </summary>
        [DataMember]
        public string Type;

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            // Type is a plain JSON string, so the serializer's type binder does not migrate it.
            Type = ReflectionUtils.GetTypeFromAssemblyQualifiedName(Type)?.AssemblyQualifiedName ?? Type;
        }

        public LockablePropertyReference()
        {
        }

        public LockablePropertyReference(LockableProperty property)
        {
            TargetObject = new SingleSceneObjectReference(property.SceneObject.Guid);
            Type = property.GetType().AssemblyQualifiedName;
        }

        /// <summary>
        /// Returns the referenced <see cref="LockableProperty"/>.
        /// </summary>
        public LockableProperty GetProperty()
        {
            // Resolve each time so unloading and reloading a scene cannot retain an old component.
            ISceneObject target = TargetObject?.Value;
            if (target == null) return null;
            foreach (ISceneObjectProperty property in target.Properties)
            {
                if (property.GetType().AssemblyQualifiedName == Type) return property as LockableProperty;
            }
            return null;
        }
    }
}
