// Copyright (c) 2013-2019 Innoactive GmbH
// Licensed under the Apache License, Version 2.0
// Modifications copyright (c) 2021-2026 MindPort GmbH

using System.Collections.Generic;
using System.Runtime.Serialization;
using UnityEngine;
using VRBuilder.Core.RestrictiveEnvironment;
using VRBuilder.Core.Configuration.Modes;
using VRBuilder.Core.Utils;
using VRBuilder.Core.Utils.Logging;
using VRBuilder.Unity;

namespace VRBuilder.Core.Conditions
{
    internal interface IDeferredConditionConfiguration
    {
        void ConfigureBeforeActivation();
    }

    /// <summary>
    /// An implementation of <see cref="ICondition"/>. Use it as the base class for your custom conditions.
    /// </summary>
    [DataContract(IsReference = true)]
    public abstract class Condition<TData> : CompletableEntity<TData>, ICondition, ILockablePropertiesProvider, IDeferredConditionConfiguration where TData : class, IConditionData, new()
    {
        private IMode configurationMode;
        private bool configurationRequested;

        /// <inheritdoc />
        public override void Configure(IMode mode)
        {
            configurationMode = mode;
            configurationRequested = true;
            // Future steps may reference scenes which are not loaded at process setup.
            if (LifeCycle.Stage != Stage.Inactive) base.Configure(mode);
        }

        void IDeferredConditionConfiguration.ConfigureBeforeActivation()
        {
            // Resolve again on every activation: the scene may have been unloaded and reloaded.
            if (configurationRequested) base.Configure(configurationMode);
        }

        protected Condition()
        {
            if (LifeCycleLoggingConfig.Instance.LogConditions)
            {
                LifeCycle.StageChanged += (sender, args) =>
                {
                    Debug.LogFormat("{0}<b>Condition</b> <i>'{1} ({2})'</i> is <b>{3}</b>.\n", ConsoleUtils.GetTabs(2), Data.Name, GetType().Name, LifeCycle.Stage);
                };
            }
        }

        /// <inheritdoc />
        IConditionData IDataOwner<IConditionData>.Data
        {
            get
            {
                return Data;
            }
        }

        /// <inheritdoc />
        public virtual IEnumerable<LockablePropertyData> GetLockableProperties()
        {
            return PropertyReflectionHelper.ExtractLockablePropertiesFromCondition(Data);
        }
    }
}
