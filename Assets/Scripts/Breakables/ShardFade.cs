using UnityEngine;

namespace Fragile.Breakables
{
    /// <summary>Fait disparaître un éclat en douceur (rétrécit) puis le détruit.</summary>
    public class ShardFade : MonoBehaviour
    {
        float m_End;
        Vector3 m_Scale;
        const float k_Shrink = 0.8f;

        public void Begin(float lifetime) { m_End = Time.time + lifetime; m_Scale = transform.localScale; }

        void Update()
        {
            float left = m_End - Time.time;
            if (left > k_Shrink) return;
            if (left <= 0f) { Destroy(gameObject); return; }
            transform.localScale = m_Scale * (left / k_Shrink);
        }
    }
}
