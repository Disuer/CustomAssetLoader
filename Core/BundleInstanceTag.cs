using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace CustomAssetLoader.Core;

/// <summary>
/// Runtime-only provenance marker attached exclusively by this plugin's
/// bundle instantiation path. Vanilla objects never receive this component.
/// </summary>
public sealed class BundleInstanceTag : MonoBehaviour
{
    public BundleInstanceTag(IntPtr ptr) : base(ptr) { }

    public BundleInstanceTag()
        : base(ClassInjector.DerivedConstructorPointer<BundleInstanceTag>())
    {
        ClassInjector.DerivedConstructorBody(this);
    }

    /// <summary>
    /// Cache key of the mod bundle this instance was loaded from.
    /// Timeline SFX uses it so two bundles that reuse a timeline name do not share cues.
    /// </summary>
    public string BundleKey;
}
