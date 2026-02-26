using UnityEngine;

public class SpawnEffect : MonoBehaviour
{
    public float spawnEffectTime = 2;
    public float pause = 1;
    public AnimationCurve fadeIn;

    ParticleSystem ps;
    float timer = 0;
    Renderer _renderer;

    int shaderProperty;

    private void Start()
    {
        shaderProperty = Shader.PropertyToID("_cutoff");
        _renderer = GetComponent<Renderer>();
        ps = GetComponentInChildren<ParticleSystem>();

        if (fadeIn == null || fadeIn.length == 0)
        {
            fadeIn = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        }

        if (ps != null)
        {
            var main = ps.main;
            main.duration = spawnEffectTime;
            ps.Play();
        }
    }

    private void Update()
    {
        if (ps != null)
        {
            if (timer < spawnEffectTime + pause)
            {
                timer += Time.deltaTime;
            }
            else
            {
                ps.Play();
                timer = 0f;
            }
        }

        if (_renderer != null)
        {
            Material material = _renderer.material;
            if (material != null && material.HasProperty(shaderProperty))
            {
                float normalized = Mathf.InverseLerp(0f, spawnEffectTime, timer);
                material.SetFloat(shaderProperty, fadeIn.Evaluate(normalized));
            }
        }
    }
}
