using UnityEngine;

namespace Fragile.Breakables
{
    /// <summary>Ce qu'une balle peut toucher en plus du décor cassable (ex. les souris).</summary>
    public interface IHittable
    {
        /// <param name="energy">Violence du coup (0..1+), comme pour <see cref="Breakable.Shot"/>.</param>
        /// <param name="push">Poussée totale de la balle ou des plombs qui ont touché (N·s).</param>
        void Hit(Vector3 point, Vector3 direction, float energy, float push);
    }
}
