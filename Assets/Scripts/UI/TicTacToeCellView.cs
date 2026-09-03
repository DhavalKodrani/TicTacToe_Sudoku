// -----------------------------------------------------------------------------
//  TicTacToeCellView.cs
//  Visual for one of the 9 Tic-Tac-Toe cells. Reports its index to the UIManager
//  when pressed (via a VRButton) and renders its mark + win highlight.
// -----------------------------------------------------------------------------
using System;
using TTLS.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TTLS.UI
{
    public class TicTacToeCellView : MonoBehaviour
    {
        [SerializeField] private int index;
        [SerializeField] private Text markLabel;     // shows "X" / "O" / ""
        [SerializeField] private Image background;
        [SerializeField] private Image winHighlight; // optional glow, disabled by default

        public int Index => index;
        public event Action<int> OnPressed;

        // Called by the cell's VRButton.OnPressed UnityEvent (wired in inspector).
        public void HandlePress() => OnPressed?.Invoke(index);

        public void SetIndex(int i) => index = i;

        public void Render(Mark mark, Color xColor, Color oColor, Color emptyText)
        {
            if (markLabel == null) return;
            switch (mark)
            {
                case Mark.X: markLabel.text = "X"; markLabel.color = xColor; break;
                case Mark.O: markLabel.text = "O"; markLabel.color = oColor; break;
                default:     markLabel.text = "";  markLabel.color = emptyText; break;
            }
        }

        public void SetWinning(bool on, Color glow)
        {
            if (winHighlight == null) return;
            winHighlight.enabled = on;
            if (on) winHighlight.color = glow;
        }

        public void SetBackground(Color c) { if (background != null) background.color = c; }
    }
}
