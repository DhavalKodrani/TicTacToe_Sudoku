// -----------------------------------------------------------------------------
//  SudokuCellView.cs
//  Visual for one of the 81 Sudoku cells. Renders either a big value or a 3x3
//  grid of pencil-mark notes, plus selection / error / given styling. Reports
//  presses to the UIManager via its index.
//
//  The 9 note labels are optional; if you don't wire them, notes simply won't
//  render (values still work). Big value + notes reuse the same GameObjects, so
//  no allocation happens when toggling between them.
// -----------------------------------------------------------------------------
using System;
using UnityEngine;
using UnityEngine.UI;

namespace TTLS.UI
{
    public class SudokuCellView : MonoBehaviour
    {
        [SerializeField] private int index;
        [SerializeField] private Text valueLabel;      // big number
        [SerializeField] private Text[] noteLabels;    // length 9, indices 0..8 -> values 1..9
        [SerializeField] private Image background;
        [SerializeField] private Image selectionRing;  // optional highlight

        public int Index => index;
        public event Action<int> OnPressed;

        public void HandlePress() => OnPressed?.Invoke(index); // wired to VRButton
        public void SetIndex(int i) => index = i;

        /// <summary>Render a filled value (hides notes).</summary>
        public void RenderValue(int value, Color color)
        {
            if (valueLabel != null)
            {
                valueLabel.gameObject.SetActive(value != 0);
                valueLabel.text = value == 0 ? "" : value.ToString();
                valueLabel.color = color;
            }
            SetNotesActive(false);
        }

        /// <summary>Render pencil notes from a bitmask (hides the big value).</summary>
        public void RenderNotes(int mask, Color noteColor)
        {
            if (valueLabel != null) valueLabel.gameObject.SetActive(false);
            if (noteLabels == null) return;
            for (int i = 0; i < noteLabels.Length; i++)
            {
                if (noteLabels[i] == null) continue;
                bool on = (mask & (1 << i)) != 0;
                noteLabels[i].gameObject.SetActive(on);
                if (on)
                {
                    noteLabels[i].text = (i + 1).ToString();
                    noteLabels[i].color = noteColor;
                }
            }
        }

        private void SetNotesActive(bool on)
        {
            if (noteLabels == null) return;
            for (int i = 0; i < noteLabels.Length; i++)
                if (noteLabels[i] != null) noteLabels[i].gameObject.SetActive(on);
        }

        public void SetBackground(Color c) { if (background != null) background.color = c; }

        public void SetSelected(bool on, Color ringColor)
        {
            if (selectionRing == null) return;
            selectionRing.enabled = on;
            if (on) selectionRing.color = ringColor;
        }
    }
}
