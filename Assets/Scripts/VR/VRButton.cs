// -----------------------------------------------------------------------------
//  VRButton.cs
//  A thin, SDK-agnostic button facade for the spatial UI.
//
//  It exposes ONE UnityEvent (OnPressed) and fires haptics + an audio click on
//  activation. It is driven by whichever input path is present:
//
//   1) Meta Interaction SDK  -> wire the SDK's PokeInteractable /
//      RayInteractable "InteractableUnityEventWrapper.WhenSelect" (or
//      "WhenUnselect") in the inspector to call VRButton.Press(). That is the
//      recommended production path (poke + raycast, controllers + hands).
//
//   2) Fallback (Editor / flat testing) -> implements IPointerClickHandler so the
//      same button works with a mouse or the XR UI raycaster during development,
//      without requiring the Meta SDK to be installed to compile.
//
//  Keeping the SDK wiring in the scene (via UnityEvents) rather than hard
//  references means this script compiles cleanly in ANY project and the designer
//  connects interactors visually — the idiomatic Meta Interaction SDK workflow.
// -----------------------------------------------------------------------------
using TTLS.Audio;
using TTLS.Settings;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace TTLS.VR
{
    [DisallowMultipleComponent]
    public class VRButton : MonoBehaviour, IPointerClickHandler
    {
        [Tooltip("Invoked when the button is activated by poke, ray-select, or click.")]
        public UnityEvent OnPressed;

        [Header("Feedback")]
        [SerializeField] private bool playClickSfx = true;
        [SerializeField] private bool fireHaptics = true;
        [Range(0f, 1f)][SerializeField] private float hapticAmplitude = 0.4f;
        [SerializeField] private float hapticDuration = 0.05f;

        [Tooltip("Which controller to buzz. Auto picks the hand that selected it " +
                 "when available; otherwise both.")]
        [SerializeField] private VRHaptics.Hand hapticHand = VRHaptics.Hand.Both;

        [SerializeField] private bool interactable = true;

        public bool Interactable
        {
            get => interactable;
            set => interactable = value;
        }

        /// <summary>
        /// The single activation entry point. Wire Meta Interaction SDK
        /// InteractableUnityEventWrapper.WhenSelect -> this method in the inspector.
        /// </summary>
        public void Press()
        {
            if (!interactable) return;

            if (playClickSfx) AudioManager.Instance?.Play(Sfx.ButtonClick);
            if (fireHaptics && (SettingsManager.Instance?.Haptics ?? true))
                VRHaptics.Pulse(hapticHand, hapticAmplitude, hapticDuration);

            OnPressed?.Invoke();
        }

        // Editor / flat fallback so UI is testable without the headset.
        public void OnPointerClick(PointerEventData eventData) => Press();
    }
}
