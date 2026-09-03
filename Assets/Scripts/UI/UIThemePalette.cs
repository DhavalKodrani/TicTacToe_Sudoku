// -----------------------------------------------------------------------------
//  UIThemePalette.cs
//  High-contrast colour sets tuned for VR lens readability. Two ScriptableObject
//  instances (Dark / Light) are assigned to the UIManager; switching themes just
//  swaps which palette is active and re-tints the visible board.
// -----------------------------------------------------------------------------
using UnityEngine;

namespace TTLS.UI
{
    [CreateAssetMenu(menuName = "TTLS/UI Theme Palette", fileName = "UIThemePalette")]
    public class UIThemePalette : ScriptableObject
    {
        [Header("Surfaces")]
        public Color panel = new Color(0.10f, 0.11f, 0.15f, 0.96f);
        public Color cell = new Color(0.16f, 0.17f, 0.22f, 1f);
        public Color cellAlt = new Color(0.13f, 0.14f, 0.19f, 1f); // 3x3 box shading
        public Color cellSelected = new Color(0.20f, 0.34f, 0.55f, 1f);

        [Header("Text")]
        public Color textPrimary = new Color(0.95f, 0.96f, 0.98f, 1f);
        public Color textGiven = new Color(0.80f, 0.86f, 1f, 1f);   // fixed clues
        public Color textEntered = new Color(0.55f, 0.85f, 1f, 1f); // player values
        public Color textNote = new Color(0.65f, 0.68f, 0.75f, 1f);
        public Color textError = new Color(1f, 0.36f, 0.38f, 1f);

        [Header("Marks (Tic-Tac-Toe)")]
        public Color markX = new Color(0.40f, 0.80f, 1f, 1f);
        public Color markO = new Color(1f, 0.55f, 0.70f, 1f);
        public Color winGlow = new Color(0.40f, 1f, 0.55f, 0.55f);

        [Header("Accents")]
        public Color selectionRing = new Color(0.40f, 0.80f, 1f, 1f);
        public Color buttonNormal = new Color(0.18f, 0.22f, 0.30f, 1f);
        public Color buttonAccent = new Color(0.24f, 0.52f, 0.86f, 1f);
    }
}
