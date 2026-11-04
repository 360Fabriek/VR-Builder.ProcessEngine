// Copyright (c) 2013-2019 Innoactive GmbH
// Licensed under the Apache License, Version 2.0
// Modifications copyright (c) 2021-2026 MindPort GmbH

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEngine.Scripting.APIUpdating;
using VRBuilder.Core.IO;
using VRBuilder.Core.UI.Drawers.Metadata;
using VRBuilder.Core.Utils;

namespace VRBuilder.Core.Serialization.NewtonsoftJson
{
    /// <summary>
    /// This serializer uses NewtonsoftJson to serialize data, the outcome is a json file in the UTF-8 encoding.
    /// </summary>
    public class NewtonsoftJsonProcessSerializer : IProcessSerializer
    {
        protected virtual int Version { get; } = 1;
        private static readonly List<JsonConverter> CachedJsonConverters = CreateJsonConverters();
        private static readonly JsonSerializerSettings CachedProcessSerializerSettings = CreateSettings(CachedJsonConverters);
        private static readonly JsonSerializerSettings CachedStepSerializerSettings = CreateStepSerializerSettings();

        private static JsonSerializerSettings CreateSettings(IList<JsonConverter> converters)
        {
            return new JsonSerializerSettings
            {
                Converters = converters.Concat(new JsonConverter[] { new MigratedTypeConverter() }).ToList(),
                PreserveReferencesHandling = PreserveReferencesHandling.All,
                Formatting = Formatting.Indented,
                ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
                SerializationBinder = new ProcessSerializationBinder(),
                TypeNameHandling = TypeNameHandling.All
            };
        }

        private static JsonSerializerSettings CreateStepSerializerSettings()
        {
            List<JsonConverter> converters = new List<JsonConverter> { new IndividualStepTransitionConverter() };
            converters.AddRange(CachedJsonConverters);
            return CreateSettings(converters);
        }

        /// <summary>
        /// Returns the json serializer settings used by the process deserialization.
        /// </summary>
        public static JsonSerializerSettings ProcessSerializerSettings
        {
            get { return CachedProcessSerializerSettings; }
        }

        private static JsonSerializerSettings StepSerializerSettings
        {
            get { return CachedStepSerializerSettings; }
        }

        /// <summary>
        /// Creates a list of JsonConverters via reflection. It adds all JsonConverters with the <seealso cref="NewtonsoftConverterAttribute"/>
        /// will be added by default.
        /// </summary>
        /// <returns>A list of all found JsonConverters.</returns>
        private static List<JsonConverter> CreateJsonConverters()
        {
            return ReflectionUtils.GetConcreteImplementationsOf<JsonConverter>()
                .WhichHaveAttribute<NewtonsoftConverterAttribute>()
                .OrderBy(type => type.GetAttribute<NewtonsoftConverterAttribute>().Priority)
                .Select(type => ReflectionUtils.CreateInstanceOfType(type) as JsonConverter)
                .ToList();
        }

        /// <inheritdoc/>
        public virtual string Name { get; } = "Newtonsoft Json Importer";

        /// <inheritdoc/>
        public virtual string FileFormat { get; } = "json";

        protected byte[] Serialize(IEntity entity, JsonSerializerSettings settings)
        {
            JObject jObject = JObject.FromObject(entity, JsonSerializer.Create(settings));
            jObject.Add("$serializerVersion", Version);
            return new UTF8Encoding().GetBytes(jObject.ToString());
        }

        protected T Deserialize<T>(byte[] data, JsonSerializerSettings settings)
        {
            string stringData = new UTF8Encoding().GetString(data);
            return (T)JsonConvert.DeserializeObject(stringData, settings);
        }

        /// <inheritdoc/>
        public virtual byte[] ProcessToByteArray(IProcess process)
        {
            return Serialize(process, ProcessSerializerSettings);
        }

        /// <inheritdoc/>
        public virtual IProcess ProcessFromByteArray(byte[] data)
        {
            JObject dataObject = JsonConvert.DeserializeObject<JObject>(new UTF8Encoding().GetString(data), ProcessSerializerSettings);

            // Check if process was serialized with version 1
            int version = dataObject.GetValue("$serializerVersion").ToObject<int>();
            if (version != 1)
            {
                throw new Exception($"The loaded process is serialized with a serializer version {version}, which in compatible with this serializer.");
            }

            return Deserialize<IProcess>(data, ProcessSerializerSettings);
        }

        /// <inheritdoc/>
        public virtual byte[] StepToByteArray(IStep step)
        {
            return Serialize(step, StepSerializerSettings);
        }

        /// <inheritdoc/>
        public virtual IStep StepFromByteArray(byte[] data)
        {
            return Deserialize<IStep>(data, StepSerializerSettings);
        }

        /// <inheritdoc/>
        public virtual byte[] ChapterToByteArray(IChapter chapter)
        {
            return Serialize(chapter, ProcessSerializerSettings);
        }

        /// <inheritdoc/>
        public virtual IChapter ChapterFromByteArray(byte[] data)
        {
            return Deserialize<IChapter>(data, ProcessSerializerSettings);
        }

        /// <inheritdoc/>
        public virtual byte[] ManifestToByteArray(IProcessAssetManifest manifest)
        {
            JObject jObject = JObject.FromObject(manifest, JsonSerializer.Create(ProcessSerializerSettings));
            jObject.Add("$serializerVersion", Version);
            return new UTF8Encoding().GetBytes(jObject.ToString());
        }

        /// <inheritdoc/>
        public virtual IProcessAssetManifest ManifestFromByteArray(byte[] data)
        {
            return Deserialize<IProcessAssetManifest>(data, ProcessSerializerSettings);
        }

        /// <inheritdoc/>
        public virtual byte[] EntityToByteArray(IEntity entity)
        {
            return Serialize(entity, ProcessSerializerSettings);
        }

        /// <inheritdoc/>
        public virtual IEntity EntityFromByteArray(byte[] data)
        {
            return Deserialize<IEntity>(data, ProcessSerializerSettings);
        }

        private sealed class MigratedTypeConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType) => typeof(Type).IsAssignableFrom(objectType);
            public override bool CanWrite => false;

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null) return null;
                if (reader.TokenType != JsonToken.String)
                    throw new JsonSerializationException("Expected an assembly-qualified type name.");
                string typeName = (string)reader.Value;
                return ReflectionUtils.GetTypeFromAssemblyQualifiedName(typeName)
                    ?? throw new JsonSerializationException($"Could not resolve type '{typeName}'.");
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) =>
                throw new NotSupportedException();
        }

        internal class ProcessSerializationBinder : DefaultSerializationBinder
        {
            private static readonly Lazy<Type[]> MovedTypes = new Lazy<Type[]>(() => ReflectionUtils.GetAllTypes()
                .Where(type => type.IsDefined(typeof(MovedFromAttribute), false)).ToArray());

            public override Type BindToType(string assemblyName, string typeName)
            {
                if (typeName == "VRBuilder.Core.Editor.UI.Drawers.Metadata.ReorderableElementMetadata")
                {
                    return typeof(ReorderableElementMetadata);
                }

                try
                {
                    return base.BindToType(assemblyName, typeName);
                }
                catch (JsonSerializationException)
                {
                    Type movedType = ResolveMovedType(assemblyName, typeName);
                    if (movedType != null)
                    {
                        return movedType;
                    }

                    // Resolve renamed arguments inside collection and other generic type names too.
                    string migratedTypeName = System.Text.RegularExpressions.Regex.Replace(typeName,
                        @"(?<=\[)([^,\[\]]+),\s*([^\[\]]+)(?=\])",
                        match => BindToType(match.Groups[2].Value, match.Groups[1].Value).AssemblyQualifiedName);
                    if (migratedTypeName != typeName)
                    {
                        return base.BindToType(assemblyName, migratedTypeName);
                    }

                    throw;
                }
            }

            private static Type ResolveMovedType(string assemblyName, string typeName)
            {
                string sourceAssembly = string.IsNullOrEmpty(assemblyName) ? null : new AssemblyName(assemblyName).Name;
                Type resolvedType = null;
                foreach (Type type in MovedTypes.Value)
                {
                    foreach (CustomAttributeData attribute in type.GetCustomAttributesData().Where(attribute => attribute.AttributeType == typeof(MovedFromAttribute)))
                    {
                        // Unity does not expose MovedFrom's data; read its public constructor arguments.
                        IList<CustomAttributeTypedArgument> arguments = attribute.ConstructorArguments;
                        string oldAssembly = (arguments.Count == 4 ? arguments[2].Value as string : null) ?? type.Assembly.GetName().Name;
                        string oldNamespace = (arguments[arguments.Count == 1 ? 0 : 1].Value as string) ?? type.Namespace;
                        string oldName = (arguments.Count == 4 ? arguments[3].Value as string : null) ?? type.FullName.Substring(type.FullName.LastIndexOf('.') + 1);
                        string oldFullName = string.IsNullOrEmpty(oldNamespace) ? oldName : oldNamespace + "." + oldName;
                        if ((sourceAssembly != null && sourceAssembly != oldAssembly) ||
                            (typeName != oldFullName && !typeName.StartsWith(oldFullName + "+", StringComparison.Ordinal)))
                        {
                            continue;
                        }

                        // EntityData is commonly nested in the renamed behavior or condition.
                        Type candidate = type.Assembly.GetType(type.FullName + typeName.Substring(oldFullName.Length));
                        if (candidate == null)
                        {
                            continue;
                        }

                        if (resolvedType != null && resolvedType != candidate)
                        {
                            throw new JsonSerializationException($"Ambiguous migration for type '{typeName}, {assemblyName}'.");
                        }

                        resolvedType = candidate;
                    }
                }

                return resolvedType;
            }
        }
    }
}
