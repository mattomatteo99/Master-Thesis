using UnityEngine;
using UnityEngine.Splines;
using System.Collections;

public class GuidedTourMover : MonoBehaviour
{
    public SplineContainer spline;
    public float duration = 66f;

    [Header("Start Delay")]
    public float startDelay = 5f;

    [Header("Pause Settings")]
    public int pauseAtKnotIndex = 7;
    public float pauseDuration = 10f;

    [Header("VR End Rotation")]
    public bool isVRMode = false;
    public float endRotationDuration = 3f;
    private bool isRotating = false;

    [Header("VR Height Calibration")]
    [Tooltip("The desired eye height above the spline path (design reference)")]
    public float desiredEyeHeight = 1.5f;
    [Tooltip("Seconds to wait for HMD tracking to stabilize before calibrating")]
    public float calibrationDelay = 1f;
    [Tooltip("The Camera Offset child of the XR Origin - assign in Inspector")]
    public Transform cameraOffset;

    [Header("Audio Settings")]
    public AudioSource ambientAudioSource;
    public AudioSource endAudioSource;
    public float delayBeforeEndAudio = 5f;
    public float ambientFadeDuration = 2f;

    private float heightOffset;
    private float timeElapsed = 0f;
    private bool isMoving = false;
    private bool isWaitingToStart = false;
    private bool isPaused = false;
    private bool hasAlreadyPaused = false;
    private bool isCalibrated = false;

    private Vector3 startPosition;
    private Quaternion startRotation;

    void Awake()
    {
        // Default offset for 2D mode (your original value)
        heightOffset = desiredEyeHeight;

        if (spline != null)
        {
            Vector3 knotLocalPos = spline.Spline[0].Position;
            Vector3 knotWorldPos = spline.transform.TransformPoint(knotLocalPos);

            SplineUtility.Evaluate(spline.Spline, 0f, out _, out var tangent, out var up);
            tangent = spline.transform.TransformDirection(tangent);
            up = spline.transform.TransformDirection(up);

            startRotation = Quaternion.LookRotation(tangent, up);

            startPosition = knotWorldPos + new Vector3(0, heightOffset, 0);

            transform.position = startPosition;
            transform.rotation = startRotation;

            Debug.Log($"Camera Rig positioned at: {transform.position}");
        }
        else
        {
            Debug.LogError("Spline not assigned to GuidedTourMover!");
        }
    }

    void Start()
    {
        if (spline == null) return;

        isWaitingToStart = true;
        StartCoroutine(StartMovementAfterDelay());
    }

    private IEnumerator StartMovementAfterDelay()
    {
        Debug.Log($"Waiting {startDelay} seconds before starting movement...");

        // --- VR AUTO CALIBRATION ---
        if (isVRMode)
        {
            Debug.Log($"VR Mode: waiting {calibrationDelay}s for tracking to stabilize...");
            yield return new WaitForSeconds(calibrationDelay);

            CalibrateHeight();

            // Wait the remaining delay time
            float remainingDelay = startDelay - calibrationDelay;
            if (remainingDelay > 0f)
            {
                yield return new WaitForSeconds(remainingDelay);
            }
        }
        else
        {
            // 2D mode: just wait the full delay, no calibration needed
            yield return new WaitForSeconds(startDelay);
        }

        Debug.Log("Starting spline movement!");
        isWaitingToStart = false;
        isMoving = true;
    }

    private void CalibrateHeight()
    {
        if (cameraOffset == null)
        {
            Debug.LogWarning("Camera Offset not assigned! Using default height offset.");
            return;
        }

        // Find the Main Camera (HMD) inside Camera Offset
        Camera hmdCamera = cameraOffset.GetComponentInChildren<Camera>();
        if (hmdCamera == null)
        {
            Debug.LogWarning("No camera found under Camera Offset! Using default height offset.");
            return;
        }

        // Get the spline start position (ground reference)
        Vector3 knotLocalPos = spline.Spline[0].Position;
        Vector3 splineStartWorld = spline.transform.TransformPoint(knotLocalPos);

        // The HMD's current world Y position
        float hmdWorldY = hmdCamera.transform.position.y;

        // How high the HMD currently is above the spline start point
        float currentHeightAboveSpline = hmdWorldY - splineStartWorld.y;

        // Calculate the offset needed so the eyes end up at desiredEyeHeight above spline
        // We adjust the XR Origin Y so that: splineY + heightOffset + (hmdLocalY) = splineY + desiredEyeHeight
        // Therefore: heightOffset = desiredEyeHeight - hmdLocalY relative to XR Origin
        float hmdLocalY = hmdCamera.transform.position.y - transform.position.y;
        heightOffset = desiredEyeHeight - hmdLocalY;

        // Recalculate start position with calibrated offset
        startPosition = splineStartWorld + new Vector3(0, heightOffset, 0);
        transform.position = startPosition;

        isCalibrated = true;
        Debug.Log($"=== VR CALIBRATION COMPLETE ===");
        Debug.Log($"HMD World Y: {hmdWorldY}");
        Debug.Log($"HMD Local Y (relative to XR Origin): {hmdLocalY}");
        Debug.Log($"Desired eye height above spline: {desiredEyeHeight}");
        Debug.Log($"Calculated height offset: {heightOffset}");
        Debug.Log($"New start position: {startPosition}");
    }

    private IEnumerator PauseAtKnot()
    {
        isPaused = true;
        hasAlreadyPaused = true;
        Debug.Log($"Pausing at knot {pauseAtKnotIndex} for {pauseDuration} seconds...");

        yield return new WaitForSeconds(pauseDuration);

        Debug.Log("Resuming movement!");
        isPaused = false;
    }

    private IEnumerator RotateRightAtEnd()
    {
        isRotating = true;
        Debug.Log("Starting 90° rotation to the right...");

        Quaternion startRot = transform.rotation;
        Quaternion targetRot = startRot * Quaternion.Euler(0, 105, 0);

        float elapsed = 0f;

        while (elapsed < endRotationDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / endRotationDuration;

            transform.rotation = Quaternion.Slerp(startRot, targetRot, t);

            yield return null;
        }

        transform.rotation = targetRot;

        Debug.Log("Rotation complete!");
        isRotating = false;
    }

    void LateUpdate()
    {
        if (spline == null) return;

        if (isWaitingToStart)
        {
            transform.position = startPosition;
            return;
        }

        if (isPaused) return;

        if (isRotating) return;

        if (!isMoving) return;

        timeElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(timeElapsed / duration);

        if (t > 0.9f)
        {
            Debug.Log($"Progress: t = {t}, timeElapsed = {timeElapsed}, duration = {duration}");
        }

        int totalKnots = spline.Spline.Count;
        float knotProgress = t * (totalKnots - 1);
        int currentKnotIndex = Mathf.FloorToInt(knotProgress);

        if (!hasAlreadyPaused && currentKnotIndex >= pauseAtKnotIndex)
        {
            float pauseT = (float)pauseAtKnotIndex / (totalKnots - 1);

            SplineUtility.Evaluate(spline.Spline, pauseT, out var pauseLocalPos, out _, out _);
            Vector3 pauseWorldPos = spline.transform.TransformPoint(pauseLocalPos);
            pauseWorldPos = pauseWorldPos + new Vector3(0, heightOffset, 0);
            transform.position = pauseWorldPos;

            timeElapsed = pauseT * duration;

            StartCoroutine(PauseAtKnot());
            return;
        }

        SplineUtility.Evaluate(spline.Spline, t, out var localPos, out var tangent, out var up);
        Vector3 worldPos = spline.transform.TransformPoint(localPos);

        worldPos = worldPos + new Vector3(0, heightOffset, 0);

        transform.position = worldPos;

        if (t >= 1f)
        {
            Debug.Log("REACHED END: Calling OnTourEnd()");
            isMoving = false;
            OnTourEnd();
        }
    }

    void OnTourEnd()
    {
        Debug.Log("Reached destination.");

        if (isVRMode)
        {
            StartCoroutine(RotateRightAtEnd());
        }

        if (ambientAudioSource != null && ambientAudioSource.isPlaying)
        {
            StartCoroutine(FadeOutAmbientAudio());
        }

        if (endAudioSource != null)
        {
            Invoke(nameof(PlayEndAudio), delayBeforeEndAudio);
        }
    }

    private IEnumerator FadeOutAmbientAudio()
    {
        if (ambientAudioSource == null) yield break;

        float startVolume = ambientAudioSource.volume;
        float elapsed = 0f;

        while (elapsed < ambientFadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / ambientFadeDuration);
            ambientAudioSource.volume = Mathf.Lerp(startVolume, 0f, t);
            yield return null;
        }

        ambientAudioSource.volume = 0f;
        ambientAudioSource.Stop();
    }

    private void PlayEndAudio()
    {
        if (endAudioSource == null) return;
        endAudioSource.Play();
    }
}