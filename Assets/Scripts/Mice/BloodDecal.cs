using System.Collections;
using UnityEngine;

namespace Fragile.Mice
{
    /// <summary>Tache de sang : s'étale (flaque) ou apparaît d'un coup (giclée), puis s'efface au bout d'un moment.</summary>
    public class BloodDecal : MonoBehaviour
    {
        const float Life = 120f;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        MaterialPropertyBlock m_Block;
        Coroutine m_Run;

        public void Play(float size, float growTime)
        {
            if (m_Run != null) StopCoroutine(m_Run);
            m_Run = StartCoroutine(Run(size, growTime));
        }

        IEnumerator Run(float size, float growTime)
        {
            var r = GetComponent<MeshRenderer>();
            m_Block ??= new MaterialPropertyBlock();
            var baseColor = r.sharedMaterial.GetColor(BaseColor);
            SetColor(r, baseColor, 1f);
            var parentScale = transform.parent != null ? transform.parent.lossyScale.x : 1f;
            // Étalement rapide au début, qui ralentit (le sang s'écoule puis s'arrête).
            for (float t = 0f; t < growTime; t += Time.deltaTime)
            {
                float k = 1f - Mathf.Pow(1f - t / growTime, 3f);
                transform.localScale = Vector3.one * Mathf.Lerp(size * 0.25f, size, k) / parentScale;
                yield return null;
            }
            transform.localScale = Vector3.one * size / parentScale;
            yield return new WaitForSeconds(Life);
            for (float t = 0f; t < 3f; t += Time.deltaTime) { SetColor(r, baseColor, 1f - t / 3f); yield return null; }
            gameObject.SetActive(false);
        }

        void SetColor(Renderer r, Color c, float alpha)
        {
            r.GetPropertyBlock(m_Block);
            c.a *= alpha;
            m_Block.SetColor(BaseColor, c);
            r.SetPropertyBlock(m_Block);
        }
    }
}
