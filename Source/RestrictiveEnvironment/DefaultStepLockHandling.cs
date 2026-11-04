// Copyright (c) 2013-2019 Innoactive GmbH
// Licensed under the Apache License, Version 2.0
// Modifications copyright (c) 2021-2026 MindPort GmbH

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;
using VRBuilder.Core.Configuration;
using VRBuilder.Core.Configuration.Modes;
using VRBuilder.Core.Properties;
using VRBuilder.Core.SceneObjects;
using VRBuilder.Core.Utils;
using VRBuilder.Unity;

namespace VRBuilder.Core.RestrictiveEnvironment
{
    /// <summary>
    /// Restricts interaction with scene objects by using LockableProperties, which are extracted from the <see cref="IStepData"/>.
    /// </summary>
    public class DefaultStepLockHandling : StepLockHandlingStrategy
    {
        private bool lockOnProcessStart = true;
        private bool lockOnProcessFinished = true;
        private IProcess runningProcess;
        private readonly Dictionary<IStepData, LockablePropertyData[]> unlockedSteps = new Dictionary<IStepData, LockablePropertyData[]>();

        /// <inheritdoc />
        public override void Unlock(IStepData data, IEnumerable<LockablePropertyData> manualUnlocked)
        {
            LockablePropertyData[] manualProperties = manualUnlocked.ToArray();
            unlockedSteps[data] = manualProperties;
            IEnumerable<LockablePropertyData> unlockList = GetUnlockProperties(data, manualProperties);

            foreach (LockablePropertyData lockable in unlockList)
            {
                if (!lockable.Property.IsAlwaysUnlocked)
                {
                    lockable.Property.RequestLocked(false, data);
                }
            }
        }

        /// <inheritdoc />
        public override void Lock(IStepData data, IEnumerable<LockablePropertyData> manualUnlocked)
        {
            // Include permissions resolved after an additive scene loaded, even if the caller cached its list earlier.
            if (unlockedSteps.TryGetValue(data, out LockablePropertyData[] previousManualProperties))
            {
                manualUnlocked = manualUnlocked.Union(previousManualProperties);
            }
            unlockedSteps.Remove(data);
            IEnumerable<LockablePropertyData> lockList = GetUnlockProperties(data, manualUnlocked);

            ITransition completedTransition = data.Transitions.Data.Transitions.FirstOrDefault(transition => transition.IsCompleted);
            if (completedTransition != null)
            {
                IStepData nextStepData = GetNextStep(completedTransition);
                LockablePropertyData[] nextStepPropertyArray = GetUnlockProperties(nextStepData, Enumerable.Empty<LockablePropertyData>()).ToArray();
                if (completedTransition is ILockablePropertiesProvider completedLockableTransition)
                {
                    IEnumerable<LockablePropertyData> transitionLockList = completedLockableTransition.GetLockableProperties();
                    LockablePropertyData[] transitionLockListArray = transitionLockList as LockablePropertyData[] ?? transitionLockList.ToArray();

                    foreach (LockablePropertyData lockable in transitionLockListArray)
                    {
                        if (!lockable.Property.IsAlwaysUnlocked)
                        {
                            lockable.Property.RequestLocked(lockable.EndStepLocked && nextStepPropertyArray.Contains(lockable) == false, data);
                            lockable.Property.RemoveUnlocker(data);
                        }
                    }

                    // Remove all lockable from completed transition
                    lockList = lockList.Except(transitionLockListArray);
                }

                // Whether we lock the property or not, we remove the current step from the unlockers so it can be locked again in the future
                LockablePropertyData[] lockablePropertyArray = lockList as LockablePropertyData[] ?? lockList.ToArray();
                foreach (LockablePropertyData lockable in lockablePropertyArray)
                {
                    lockable.Property.RemoveUnlocker(data);
                }

                // Remove properties that stay unlocked from the list.
                lockList = lockablePropertyArray.Except(nextStepPropertyArray);
            }

            foreach (LockablePropertyData lockable in lockList)
            {
                // Fallback check if the property has the IsAlwaysUnlocked flag
                if (!lockable.Property.IsAlwaysUnlocked)
                {
                    lockable.Property.RequestLocked(true, data);
                }
            }
        }

        private IEnumerable<LockablePropertyData> GetUnlockProperties(IStepData data, IEnumerable<LockablePropertyData> manualUnlocked)
        {
            IEnumerable<LockablePropertyData> properties = PropertyReflectionHelper.ExtractLockablePropertiesFromStep(data).Union(manualUnlocked);
            if (data is ILockableStepData lockableStepData)
            {
                foreach (LockablePropertyReference reference in lockableStepData.ToUnlock)
                {
                    ISceneObject sceneObject = reference.TargetObject.Value;
                    if (sceneObject != null)
                    {
                        properties = properties.Union(sceneObject.Properties.OfType<LockableProperty>()
                            .Where(property => property.GetType().AssemblyQualifiedName == reference.Type)
                            .Select(property => new LockablePropertyData(property)));
                    }
                }

                foreach (Guid tag in lockableStepData.GroupsToUnlock.Keys)
                {
                    foreach (ISceneObject sceneObject in RuntimeConfigurator.Configuration.SceneObjectRegistry.GetObjects(tag))
                    {
                        properties = properties.Union(sceneObject.Properties.OfType<LockableProperty>()
                            .Where(property => lockableStepData.GroupsToUnlock[tag].Contains(property.GetType()))
                            .Select(property => new LockablePropertyData(property)));
                    }
                }
            }

            return properties.Where(property => property.Property != null);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (runningProcess == null)
            {
                return;
            }

            ISceneObjectRegistry registry = RuntimeConfigurator.Configuration.SceneObjectRegistry;
            foreach (UnityEngine.GameObject root in scene.GetRootGameObjects())
            {
                foreach (ProcessSceneObject sceneObject in root.GetComponentsInChildren<ProcessSceneObject>(true))
                {
                    // Register also inactive roots, without adding duplicate registry event subscriptions.
                    if (!registry.GetObjects(sceneObject.Guid).Contains(sceneObject))
                    {
                        registry.Register(sceneObject);
                    }
                }

                if (lockOnProcessStart)
                {
                    foreach (LockableProperty property in root.GetComponentsInChildren<LockableProperty>(true))
                    {
                        if (property.InheritSceneObjectLockState && !property.IsAlwaysUnlocked)
                        {
                            property.SetLocked(true);
                        }
                    }
                }
            }

            foreach (KeyValuePair<IStepData, LockablePropertyData[]> step in unlockedSteps.ToArray())
            {
                foreach (LockablePropertyData property in GetUnlockProperties(step.Key, step.Value))
                {
                    if (property.Property.gameObject.scene == scene && !property.Property.IsAlwaysUnlocked)
                    {
                        property.Property.RequestLocked(false, step.Key);
                    }
                }
            }
        }

        private void OnProcessStageChanged(object sender, ActivationStateChangedEventArgs args)
        {
            if (args.Stage == Stage.Inactive)
            {
                StopSceneHandling();
            }
        }

        private void StopSceneHandling()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (runningProcess != null)
            {
                runningProcess.LifeCycle.StageChanged -= OnProcessStageChanged;
                runningProcess = null;
            }
            unlockedSteps.Clear();
        }

        private IStepData GetNextStep(ITransition completedTransition)
        {
            IStep targetStep = completedTransition.Data.TargetStepReference.Entity;
            if (targetStep != null)
            {
                return targetStep.Data;
            }

            if (ProcessRunner.IsRunning == false)
            {
                return null;
            }

            IProcessData process = ProcessRunner.Current.Data;
            // Test all chapters, but the last.
            for (int i = 0; i < process.Chapters.Count - 1; i++)
            {
                if (process.Chapters[i] == process.Current)
                {
                    if (process.Chapters[i + 1].Data.FirstStep != null)
                    {
                        return process.Chapters[i + 1].Data.FirstStep.Data;
                    }
                    break;
                }
            }
            // No next step found, seems to be the last.
            return null;
        }

        /// <inheritdoc />
        public override void Configure(IMode mode)
        {
            if (mode.ContainsParameter<bool>("LockOnProcessStart"))
            {
                lockOnProcessStart = mode.GetParameter<bool>("LockOnProcessStart");
            }

            if (mode.ContainsParameter<bool>("LockOnProcessFinished"))
            {
                lockOnProcessFinished = mode.GetParameter<bool>("LockOnProcessFinished");
            }
        }

        /// <inheritdoc />
        public override void OnProcessStarted(IProcess process)
        {
            StopSceneHandling();
            runningProcess = process;
            runningProcess.LifeCycle.StageChanged += OnProcessStageChanged;
            SceneManager.sceneLoaded += OnSceneLoaded;

            if (lockOnProcessStart)
            {
                foreach (LockableProperty prop in SceneUtils.GetActiveAndInactiveComponents<LockableProperty>())
                {
                    if(prop.InheritSceneObjectLockState && !prop.IsAlwaysUnlocked)
                    {
                        prop.SetLocked(true);
                    }
                }
            }
        }

        /// <inheritdoc />
        public override void OnProcessFinished(IProcess process)
        {
            StopSceneHandling();

            if (lockOnProcessFinished)
            {
                foreach (LockableProperty prop in SceneUtils.GetActiveAndInactiveComponents<LockableProperty>())
                {
                    if(prop.InheritSceneObjectLockState && !prop.IsAlwaysUnlocked)
                    {
                        prop.SetLocked(true);
                    }
                }
            }
        }
    }
}
