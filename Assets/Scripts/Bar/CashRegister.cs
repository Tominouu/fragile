using System.Collections;
using System.Collections.Generic;
using Fragile.Audio;
using Fragile.Grab;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fragile.Bar
{
    /// <summary>
    /// Caisse enregistreuse : les touches tapent un montant sur l'écran ; la touche rouge l'encaisse (« ka-ching »)
    /// et éjecte le tiroir-caisse.
    /// </summary>
    public class CashRegister : MonoBehaviour
    {
        [SerializeField] TextMeshPro m_Display;
        [SerializeField] SlidingDrawer m_Drawer;
        [SerializeField] Renderer m_Screen;

        string m_Entry = "";
        Coroutine m_Message;

        public void Setup(TextMeshPro display, SlidingDrawer drawer, Renderer screen) { m_Display = display; m_Drawer = drawer; m_Screen = screen; }

        void Start() => Show("0.00");

        public void Key(int digit)
        {
            Sfx.PlayAt(Sfx.Click, transform.position, 0.6f, Random.Range(0.95f, 1.05f));
            Sfx.PlayAt(Sfx.Beep, transform.position, 0.25f);
            if (m_Message != null) { StopCoroutine(m_Message); m_Message = null; }
            if (m_Entry.Length >= 6) m_Entry = "";
            m_Entry += digit.ToString();
            Show(Price(m_Entry));
        }

        public void Total()
        {
            Sfx.PlayAt(Sfx.Click, transform.position, 0.6f);
            Sfx.PlayAt(Sfx.Bell, transform.position, 0.8f);
            if (m_Drawer != null) m_Drawer.Open();
            if (m_Message != null) StopCoroutine(m_Message);
            m_Message = StartCoroutine(TotalMessage(m_Entry.Length > 0 ? Price(m_Entry) : "0.00"));
            m_Entry = "";
        }

        IEnumerator TotalMessage(string amount)
        {
            for (int i = 0; i < 3; i++) { Show("TOTAL"); yield return new WaitForSeconds(0.35f); Show(amount); yield return new WaitForSeconds(0.35f); }
            yield return new WaitForSeconds(1.5f);
            Show("0.00");
            m_Message = null;
        }

        static string Price(string digits)
        {
            var d = digits.PadLeft(3, '0');
            return d.Substring(0, d.Length - 2).TrimStart('0').PadLeft(1, '0') + "." + d.Substring(d.Length - 2);
        }

        void Show(string text) { if (m_Display != null) m_Display.text = text; }
    }
}
