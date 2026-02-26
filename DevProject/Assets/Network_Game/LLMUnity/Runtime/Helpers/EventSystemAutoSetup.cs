using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Automatically sets up the correct Input Module for the EventSystem based on which input system is active.
/// This is not needed in your project ,you can use your own (or default) EventSystem
/// </summary>
namespace LLMUnity
{
    [RequireComponent(typeof(EventSystem))]
    [DefaultExecutionOrder(-1000)]
    public class EventSystemAutoSetup : MonoBehaviour
    {
        void Awake()
        {
            SetupInputModule();
        }

        private void SetupInputModule()
        {
            var eventSystem = GetComponent<EventSystem>();

            // Try to find and use the new Input System module
            Type inputSystemModuleType = FindInputSystemModuleType();

            if (inputSystemModuleType != null)
            {
                // New Input System is available
                // Remove old module if present
                var oldModule = eventSystem.GetComponent<StandaloneInputModule>();
                if (oldModule != null)
                    DestroyImmediate(oldModule);

                // Add new module if not present
                if (eventSystem.GetComponent(inputSystemModuleType) == null)
                    eventSystem.gameObject.AddComponent(inputSystemModuleType);

                // Some scenes end up with a partially configured InputSystemUIInputModule
                // (actions asset assigned, but Point/Click/Submit/etc references missing).
                // That breaks focusing UI Toolkit TextFields, so heal it automatically.
                var newModule = eventSystem.GetComponent(inputSystemModuleType);
                EnsureInputSystemUiActionsConfigured(newModule);
            }
            else
            {
                // Legacy Input System only
                // Remove new module if present (shouldn't happen, but just in case)
                Type newModuleType = Type.GetType(
                    "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem"
                );
                if (newModuleType != null)
                {
                    var newModule = eventSystem.GetComponent(newModuleType);
                    if (newModule != null)
                    {
                        DestroyImmediate(newModule);
                    }
                }

                // Add legacy module if not present
                if (eventSystem.GetComponent<StandaloneInputModule>() == null)
                    eventSystem.gameObject.AddComponent<StandaloneInputModule>();
            }
        }

        private Type FindInputSystemModuleType()
        {
            // Try to find InputSystemUIInputModule type
            Type type = Type.GetType(
                "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem"
            );
            return type;
        }

        private void EnsureInputSystemUiActionsConfigured(Component inputModule)
        {
            if (inputModule == null)
                return;

            try
            {
                Type moduleType = inputModule.GetType();

                bool missingCriticalBinding =
                    IsNullProperty(moduleType, inputModule, "point")
                    || IsNullProperty(moduleType, inputModule, "leftClick")
                    || IsNullProperty(moduleType, inputModule, "move")
                    || IsNullProperty(moduleType, inputModule, "submit")
                    || IsNullProperty(moduleType, inputModule, "cancel");

                if (!missingCriticalBinding)
                    return;

                var assignDefaults = moduleType.GetMethod("AssignDefaultActions");
                if (assignDefaults != null)
                {
                    assignDefaults.Invoke(inputModule, null);
                    Debug.Log(
                        "[EventSystemAutoSetup] Repaired InputSystemUIInputModule by assigning default UI actions."
                    );
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[EventSystemAutoSetup] Failed to auto-configure InputSystemUIInputModule actions: {ex.Message}"
                );
            }
        }

        private static bool IsNullProperty(Type type, object instance, string propertyName)
        {
            var prop = type.GetProperty(propertyName);
            if (prop == null)
                return true;
            return prop.GetValue(instance) == null;
        }
    }
}
