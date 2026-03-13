using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// Makes sample-scene VFX visible by moving them to a child anchor above the host object.
/// Useful for top-down ML-Agents example scenes where VFX on the mesh origin are visually buried.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(VisualEffect))]
public sealed class VisibleSampleVfxAnchor : MonoBehaviour
{
    [SerializeField]
    private Vector3 m_LocalOffset = new Vector3(0f, 0.85f, 0f);

    [SerializeField]
    [Min(0.1f)]
    private float m_ScaleMultiplier = 2f;

    [SerializeField]
    private string m_AnchorName = "__VisibleVfxAnchor";

    private void OnEnable()
    {
        EnsureAnchor();
    }

    private void OnValidate()
    {
        EnsureAnchor();
    }

    private void EnsureAnchor()
    {
        VisualEffect source = GetComponent<VisualEffect>();
        if (source == null || source.visualEffectAsset == null)
        {
            return;
        }

        Transform anchor = transform.Find(m_AnchorName);
        if (anchor == null)
        {
            var anchorObject = new GameObject(m_AnchorName);
            anchorObject.transform.SetParent(transform, false);
            anchor = anchorObject.transform;
        }

        anchor.localPosition = m_LocalOffset;
        anchor.localRotation = Quaternion.identity;
        anchor.localScale = Vector3.one * Mathf.Max(0.1f, m_ScaleMultiplier);

        VisualEffect anchoredEffect = anchor.GetComponent<VisualEffect>();
        if (anchoredEffect == null)
        {
            anchoredEffect = anchor.gameObject.AddComponent<VisualEffect>();
        }

        SyncEffect(source, anchoredEffect);

        VFXRenderer sourceRenderer = GetComponent<VFXRenderer>();
        if (sourceRenderer != null)
        {
            sourceRenderer.enabled = false;
        }

        source.enabled = false;
    }

    private static void SyncEffect(VisualEffect source, VisualEffect target)
    {
        if (target == null || source == null)
        {
            return;
        }

        if (target.visualEffectAsset != source.visualEffectAsset)
        {
            target.visualEffectAsset = source.visualEffectAsset;
        }

        target.playRate = source.playRate;
        target.startSeed = source.startSeed;
        target.resetSeedOnPlay = source.resetSeedOnPlay;
        target.pause = false;

        if (!target.enabled)
        {
            target.enabled = true;
        }

        target.Reinit();
        target.Play();
    }
}
