using UnityEngine;

namespace Fragile.Weapons
{
    /// <summary>
    /// Zone (déclencheur) où l'on introduit les munitions : trappe sous le fusil à pompe, fenêtre de la carabine
    /// (verrou ouvert), puits de chargeur du pistolet. Transmet la munition à l'arme.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class LoadZone : MonoBehaviour
    {
        [SerializeField] Firearm m_Gun;

        void Awake()
        {
            if (m_Gun == null) m_Gun = GetComponentInParent<Firearm>();
            GetComponent<Collider>().isTrigger = true;
        }

        void OnTriggerEnter(Collider other) => Offer(other);
        void OnTriggerStay(Collider other) => Offer(other);   // ex. on ouvre le verrou avec la cartouche déjà devant

        void Offer(Collider other)
        {
            var body = other.attachedRigidbody;
            var item = body != null ? body.GetComponent<AmmoItem>() : other.GetComponentInParent<AmmoItem>();
            if (item != null && !item.consumed && item.held) m_Gun.TryLoad(item);
        }
    }
}
