// -----------------------------------------------------------------------------
//  VRHaptics.cs
//  Controller haptics abstraction. Uses OVRInput when the Oculus/Meta XR plugin
//  is present (define OVR_PLUGIN_PRESENT or the Oculus asmdef symbol), and falls
//  back to the Unity XR Input Subsystem otherwise, so it works on Quest with the
//  Meta XR SDK AND compiles in a bare project.
//
//  Wrapped in try/catch: haptics are "nice to have"; a missing device must never
//  throw during gameplay.
// -----------------------------------------------------------------------------
using UnityEngine;
#if !OVR_PLUGIN_PRESENT
using UnityEngine.XR;
using System.Collections.Generic;
#endif

namespace TTLS.VR
{
    public static class VRHaptics
    {
        public enum Hand { Left, Right, Both }

        public static void Pulse(Hand hand, float amplitude = 0.4f, float duration = 0.05f)
        {
            amplitude = Mathf.Clamp01(amplitude);
            try
            {
#if OVR_PLUGIN_PRESENT
                // ---- Meta / Oculus path (recommended on Quest) ----
                if (hand == Hand.Left || hand == Hand.Both)
                    OVRInput.SetControllerVibration(1f, amplitude, OVRInput.Controller.LTouch);
                if (hand == Hand.Right || hand == Hand.Both)
                    OVRInput.SetControllerVibration(1f, amplitude, OVRInput.Controller.RTouch);
                CoroutineHost.StopAfter(duration, hand);
#else
                // ---- Generic Unity XR path ----
                if (hand == Hand.Left || hand == Hand.Both)
                    SendImpulse(XRNode.LeftHand, amplitude, duration);
                if (hand == Hand.Right || hand == Hand.Both)
                    SendImpulse(XRNode.RightHand, amplitude, duration);
#endif
            }
            catch (System.Exception e)
            {
                // Never let a haptics hiccup interrupt play.
                Debug.LogWarning($"[VRHaptics] Pulse failed: {e.Message}");
            }
        }

#if !OVR_PLUGIN_PRESENT
        private static readonly List<InputDevice> _devices = new List<InputDevice>();

        private static void SendImpulse(XRNode node, float amplitude, float duration)
        {
            _devices.Clear();
            InputDevices.GetDevicesAtXRNode(node, _devices);
            for (int i = 0; i < _devices.Count; i++)
            {
                if (_devices[i].TryGetHapticCapabilities(out var caps) && caps.supportsImpulse)
                    _devices[i].SendHapticImpulse(0u, amplitude, duration);
            }
        }
#endif
    }

#if OVR_PLUGIN_PRESENT
    // Tiny helper to stop OVR vibration after a duration without a per-call coroutine
    // object leak. A single persistent host runs the timers.
    internal class CoroutineHost : MonoBehaviour
    {
        private static CoroutineHost _inst;
        private static CoroutineHost Inst
        {
            get
            {
                if (_inst == null)
                {
                    var go = new GameObject("VRHapticsHost");
                    Object.DontDestroyOnLoad(go);
                    _inst = go.AddComponent<CoroutineHost>();
                }
                return _inst;
            }
        }

        public static void StopAfter(float t, VRHaptics.Hand hand) =>
            Inst.StartCoroutine(Inst.Stop(t, hand));

        private System.Collections.IEnumerator Stop(float t, VRHaptics.Hand hand)
        {
            yield return new WaitForSeconds(t);
            if (hand == VRHaptics.Hand.Left || hand == VRHaptics.Hand.Both)
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
            if (hand == VRHaptics.Hand.Right || hand == VRHaptics.Hand.Both)
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
        }
    }
#endif
}
