// Assets/Scripts/UI scripts/MapModeController.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.EventSystems;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem; // optional fallback reads are harmless
#endif

[DisallowMultipleComponent]
public class MapModeController : MonoBehaviour
{
    public static MapModeController Instance { get; private set; }

    [Header("References")]
    public Camera mapCamera;              // orthographic, disabled until open
    public Canvas mapCanvas;              // Screen Space - Overlay recommended (CanvasGroup on root)
    public RectTransform iconContainer;   // where icon buttons live
    public GameObject iconButtonPrefab;
    public Graphic selectedNameText;

    [Header("Goal Pointer (optional)")]
    public GoalPointerAdapter goalPointer;

    [Header("Pan / Zoom")]
    [Tooltip("Keyboard WASD/Arrows pan speed.")]
    public float panSpeedKeyboard = 25f;
    [Tooltip("Gamepad left-stick pan speed.")]
    public float panSpeedGamepad = 25f;
    [Tooltip("Mouse drag pan multiplier. 1 = 1:1, >1 amplifies.")]
    public float panDragSpeed = 1.0f;
    [Tooltip("Zoom speed (affects RT/LT and mouse wheel).")]
    public float zoomSpeed = 200f;
    public float minOrthoSize = 10f;
    public float maxOrthoSize = 200f;
    public float startOrthoSize = 60f;

    [Header("World Bounds (optional)")]
    public Bounds worldBounds;
    public bool clampToWorldBounds = false;

    [Header("Diagnostics")]
    public bool diagLogs = false;

    // --- Right-stick fake mouse cursor ---
    [Header("Stick Cursor (Right Stick)")]
    [Tooltip("Fake cursor image RectTransform on the map canvas.")]
    public RectTransform cursor;
    [Tooltip("Canvas that contains the cursor (defaults to mapCanvas).")]
    public Canvas cursorCanvas;
    [Tooltip("Cursor movement speed in UI pixels/second.")]
    public float cursorSpeed = 1200f;
    [Tooltip("Hold RB to multiply cursor speed by this factor.")]
    public float rbSpeedBoost = 1.8f;
    [Tooltip("Invert Right Stick Y for cursor movement.")]
    public bool invertCursorY = true;
    [Tooltip("Hide the OS cursor while Map Mode is open (use fake cursor only).")]
    public bool hideSystemCursorInMapMode = true;

    [Tooltip("GraphicRaycasters to target UI under cursor. If empty, uses mapCanvas's raycaster.")]
    public List<GraphicRaycaster> raycasters = new List<GraphicRaycaster>();
    [Tooltip("Key used to 'click' under the fake cursor (A button also works).")]
    public KeyCode selectKey = KeyCode.Return;

    [Header("Cursor – Debugging Helpers")]
    [Tooltip("Also move fake cursor from mouse delta (helps prove canvas/cursor wiring).")]
    public bool enableMouseDeltaAssist = false;
    [Tooltip("Mouse assist multiplier relative to cursorSpeed.")]
    public float mouseAssistMultiplier = 30f;
    [Tooltip("Spam a short log when RS is zero while map is open.")]
    public bool logZeroRightStick = false;

    [Header("Cursor – Flick Snap (Optional)")]
    [Tooltip("If enabled, a quick right-stick flick snaps the cursor to the nearest visible icon.")]
    public bool enableFlickSnap = true;
    [Range(0.1f, 1f)] public float flickMagnitudeThreshold = 0.75f;
    [Range(0.03f, 0.4f)] public float flickTimeWindow = 0.15f;

    [Header("Input Modality (Auto Switch)")]
    [Tooltip("If true, show fake cursor only when recent gamepad input is detected; otherwise show OS cursor.")]
    public bool autoSwitchCursorByLastInput = true;
    [Tooltip("Seconds to consider the last input as the 'active' modality.")]
    public float inputActivityWindow = 2.0f;

    // --- Runtime state ---
    private readonly Dictionary<MapKeyArea, MapIconRuntime> _icons =
        new Dictionary<MapKeyArea, MapIconRuntime>();

    private bool _mapOpen;

    // Drag state
    private bool _dragArmed;
    private bool _dragging;
    private Vector2 _dragStartScreen;
    private Vector3 _dragStartCamPos;
    private const float DRAG_DEADZONE_PX = 3f;

    // Restore state
    private float _prevTimeScale = 1f;
    private bool _hadPrevCursor;
    private CursorLockMode _prevLock;

    // Cursor internals
    private RectTransform _canvasRect;
    private Vector2 _cursorPos; // anchoredPosition
    private EventSystem _eventSystem;

    // Flick detection
    private float _flickTimer = 0f;
    private bool _flickArmed = false;

    // Input modality tracking
    private float _lastGamepadActivityTime = -999f;
    private float _lastMouseKeyboardActivityTime = -999f;
    private bool _usingGamepadNow = false; // current modality while map is open

    // Small interface to support either Text or TMP_Text without a hard dependency
    interface IHasText { string text { get; set; } }
    class UnityTextWrapper : IHasText
    {
        private readonly object _ref;
        public UnityTextWrapper(object r) { _ref = r; }
        public string text
        {
            get
            {
                if (_ref is TMPro.TMP_Text) return ((TMPro.TMP_Text)_ref).text;
                if (_ref is UnityEngine.UI.Text) return ((UnityEngine.UI.Text)_ref).text;
                return string.Empty;
            }
            set
            {
                if (_ref is TMPro.TMP_Text) ((TMPro.TMP_Text)_ref).text = value;
                else if (_ref is UnityEngine.UI.Text) ((UnityEngine.UI.Text)_ref).text = value;
            }
        }
    }
    private IHasText _selectedName;

    private class MapIconRuntime
    {
        public MapKeyArea area;
        public RectTransform rt;
        public Button button;
        public Image image;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("[MapModeController] Duplicate Instance on " + name + ". Destroying this component.");
            Destroy(this);
            return;
        }
        Instance = this;

        if (mapCanvas != null)
        {
            var cg = mapCanvas.GetComponent<CanvasGroup>();
            if (cg == null) cg = mapCanvas.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 0f; cg.interactable = false; cg.blocksRaycasts = false;
        }

        if (mapCamera != null)
        {
            mapCamera.enabled = false;
            mapCamera.orthographic = true;
            mapCamera.orthographicSize = startOrthoSize;
        }

        if (selectedNameText != null)
            _selectedName = new UnityTextWrapper(selectedNameText);

        // Cursor wiring
        if (cursorCanvas == null) cursorCanvas = mapCanvas;
        if (cursorCanvas != null) _canvasRect = cursorCanvas.transform as RectTransform;
        _eventSystem = EventSystem.current;

        if (cursor != null)
        {
            if (_canvasRect != null) _cursorPos = Vector2.zero; // center
            ApplyCursorPosition();
            cursor.gameObject.SetActive(false); // start hidden; modality will decide on open
        }

        // Default raycaster to the map canvas if none provided
        if ((raycasters == null || raycasters.Count == 0) && mapCanvas != null)
        {
            var gr = mapCanvas.GetComponent<GraphicRaycaster>();
            if (gr != null)
            {
                if (raycasters == null) raycasters = new List<GraphicRaycaster>(1);
                raycasters.Add(gr);
            }
        }
    }

    private void Update()
    {
        HandleToggleInput();

        if (!_mapOpen) return;

        // Detect which modality is currently active
        UpdateInputActivityTracking();
        UpdateCursorVisibilityForModality();

        HandlePanZoom();
        HandleCursorMoveAndClick();
        UpdateIconScreenPositions();

        if (Input.GetKeyDown(KeyCode.Escape)) CloseMap();
    }

    // --- Public API for MapKeyArea registration ---
    public void RegisterArea(MapKeyArea area)
    {
        if (!area) return;
        if (_icons.ContainsKey(area)) return;
        if (!iconButtonPrefab || !iconContainer) { Debug.LogWarning("[MapModeController] Missing icon prefab or container."); return; }

        var go = UnityEngine.Object.Instantiate(iconButtonPrefab, iconContainer);
        go.name = "MapIcon_" + area.displayName + "_" + area.areaType;
        var rt = go.GetComponent<RectTransform>();
        var btn = go.GetComponentInChildren<Button>(true) ?? go.AddComponent<Button>();
        var img = go.GetComponentInChildren<Image>(true) ?? go.AddComponent<Image>();
        if (area.iconSprite) img.sprite = area.iconSprite;
        btn.onClick.AddListener(delegate { OnIconClicked(area); });

        _icons.Add(area, new MapIconRuntime { area = area, rt = rt, button = btn, image = img });
        go.SetActive(_mapOpen);

        if (diagLogs) Debug.Log("[MapModeController] Registered " + area.displayName + " (" + area.areaType + ").");
    }

    public void UnregisterArea(MapKeyArea area)
    {
        if (!area) return;
        if (_icons.TryGetValue(area, out var rt))
        {
            if (rt.rt) UnityEngine.Object.Destroy(rt.rt.gameObject);
            _icons.Remove(area);
            if (diagLogs) Debug.Log("[MapModeController] Unregistered " + area.displayName + ".");
        }
    }

    private void OnIconClicked(MapKeyArea area)
    {
        if (area == null) return;

        if (mapCamera)
        {
            var camPos = mapCamera.transform.position;
            camPos.x = area.iconWorldAnchor.position.x;
            camPos.z = area.iconWorldAnchor.position.z;
            mapCamera.transform.position = ClampToBounds(camPos);
        }

        if (_selectedName != null)
            _selectedName.text = area.displayName;

        if (goalPointer)
            goalPointer.SetGoal(area.iconWorldAnchor, area.areaType);

        OnGoalSelected.Invoke(area.iconWorldAnchor, area.areaType);

        if (diagLogs) Debug.Log("[MapModeController] Selected goal: " + area.displayName + " (" + area.areaType + ").");
    }

    [Serializable]
    public class GoalSelectedEvent : UnityEvent<Transform, MapKeyArea.AreaType> { }
    [Header("Events")] public GoalSelectedEvent OnGoalSelected = new GoalSelectedEvent();

    private void OpenMap()
    {
        if (_mapOpen || !mapCamera || !mapCanvas) return;
        _mapOpen = true;

        _prevTimeScale = Time.timeScale;
        _prevLock = Cursor.lockState;
        _hadPrevCursor = Cursor.visible;

        var gm = GameManager.Instance ?? FindGameManagerCompat();
        bool gmPaused = false;
        if (gm != null) { try { gm.PushExternalPause(); gmPaused = true; } catch { } }
        if (!gmPaused) { Time.timeScale = 0f; AudioListener.pause = true; }

        mapCamera.enabled = true;
        var cg = mapCanvas.GetComponent<CanvasGroup>();
        if (cg) { cg.alpha = 1f; cg.interactable = true; cg.blocksRaycasts = true; }
        foreach (var kv in _icons) if (kv.Value.rt) kv.Value.rt.gameObject.SetActive(true);

        mapCamera.orthographicSize = Mathf.Clamp(startOrthoSize, minOrthoSize, maxOrthoSize);

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player)
        {
            var p = mapCamera.transform.position;
            p.x = player.transform.position.x; p.z = player.transform.position.z;
            mapCamera.transform.position = ClampToBounds(p);
        }

        // Start with both cursors hidden; modality will decide
        if (cursor != null) cursor.gameObject.SetActive(false);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true; // will be overridden below by modality

        if (_canvasRect != null) { _cursorPos = Vector2.zero; ApplyCursorPosition(); }
        _flickTimer = 0f;
        _flickArmed = false;

        // Immediately evaluate modality so the correct cursor is shown on open
        _lastGamepadActivityTime = -999f;
        _lastMouseKeyboardActivityTime = Time.unscaledTime; // default to mouse visible unless pad moves
        UpdateInputActivityTracking();
        UpdateCursorVisibilityForModality();
    }

    private void CloseMap()
    {
        if (!_mapOpen) return;
        _mapOpen = false;

        _dragArmed = _dragging = false;

        var gm = GameManager.Instance ?? FindGameManagerCompat();
        bool gmResumed = false;
        if (gm != null) { try { gm.PopExternalPause(); gmResumed = true; } catch { } }
        if (!gmResumed) { Time.timeScale = (_prevTimeScale > 0f) ? _prevTimeScale : 1f; AudioListener.pause = false; }

        Input.ResetInputAxes();

        Cursor.lockState = _prevLock;
        Cursor.visible = _hadPrevCursor;

        if (mapCamera) mapCamera.enabled = false;
        if (mapCanvas)
        {
            var cg = mapCanvas.GetComponent<CanvasGroup>();
            if (cg) { cg.alpha = 0f; cg.interactable = false; cg.blocksRaycasts = false; }
        }
        foreach (var kv in _icons) if (kv.Value.rt) kv.Value.rt.gameObject.SetActive(false);
        if (cursor != null) cursor.gameObject.SetActive(false);
    }

    private void HandleToggleInput()
    {
        bool pressed = Input.GetKeyDown(KeyCode.M) ||
                       Input.GetKeyDown(KeyCode.JoystickButton6) || // Back
                       Input.GetKeyDown(KeyCode.JoystickButton7);   // Start
#if ENABLE_INPUT_SYSTEM
        if (!pressed && Gamepad.current != null &&
            (Gamepad.current.selectButton.wasPressedThisFrame || Gamepad.current.startButton.wasPressedThisFrame))
            pressed = true;
#endif
        if (pressed) { if (_mapOpen) CloseMap(); else OpenMap(); }
    }

    // ---------- Pan + Zoom (Left stick / RT LT / mouse wheel) ----------
    private void HandlePanZoom()
    {
        if (!mapCamera) return;

        // 1) Keyboard / left stick pan (disabled while mouse-drag panning)
        if (!_dragging)
        {
            Vector2 pan = Vector2.zero;

            // Keyboard
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) pan.x += 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) pan.x -= 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) pan.y += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) pan.y -= 1f;

            // Gamepad (your unified wrapper)
            pan += GamepadInput.LeftStick;

            pan = Vector2.ClampMagnitude(pan, 1f);

            if (pan.sqrMagnitude > 0f)
            {
                Vector3 camF = mapCamera.transform.forward; camF.y = 0f; if (camF.sqrMagnitude < 1e-6f) camF = Vector3.forward; camF.Normalize();
                Vector3 camR = mapCamera.transform.right; camR.y = 0f; if (camR.sqrMagnitude < 1e-6f) camR = Vector3.right; camR.Normalize();

                Vector3 moveWorld = (camR * pan.x + camF * pan.y) * panSpeedGamepad * Time.unscaledDeltaTime;
                var p = mapCamera.transform.position + moveWorld;
                p.y = mapCamera.transform.position.y;
                mapCamera.transform.position = ClampToBounds(p);
            }
        }

        // 2) Mouse drag to pan (suppressed if clicking UI)
        if (Input.GetMouseButtonDown(0))
        {
            _dragArmed = !IsPointerOverClickableUI();
            _dragging = false;
            _dragStartScreen = Input.mousePosition;
            _dragStartCamPos = mapCamera.transform.position;
        }

        if (Input.GetMouseButton(0) && _dragArmed)
        {
            if (!_dragging)
            {
                Vector2 diff = (Vector2)Input.mousePosition - _dragStartScreen;
                if (diff.sqrMagnitude >= DRAG_DEADZONE_PX * DRAG_DEADZONE_PX)
                    _dragging = true;
            }

            if (_dragging)
            {
                Vector2 deltaPx = (Vector2)Input.mousePosition - _dragStartScreen;
                float unitsPerPixelY = (2f * mapCamera.orthographicSize) / Mathf.Max(1, Screen.height);
                float unitsPerPixelX = unitsPerPixelY * mapCamera.aspect;

                Vector3 camF = mapCamera.transform.forward; camF.y = 0f; if (camF.sqrMagnitude < 1e-6f) camF = Vector3.forward; camF.Normalize();
                Vector3 camR = mapCamera.transform.right; camR.y = 0f; if (camR.sqrMagnitude < 1e-6f) camR = Vector3.right; camR.Normalize();

                Vector3 dragWorld =
                    (-deltaPx.x * unitsPerPixelX) * camR +
                    (-deltaPx.y * unitsPerPixelY) * camF;

                Vector3 target = _dragStartCamPos + dragWorld * Mathf.Max(0f, panDragSpeed);
                target.y = mapCamera.transform.position.y;
                mapCamera.transform.position = ClampToBounds(target);
            }
        }
        if (Input.GetMouseButtonUp(0))
        {
            _dragArmed = false;
            _dragging = false;
        }

        // 3) Zoom: RT in / LT out + mouse wheel
        if (!_dragging)
        {
            float size = mapCamera.orthographicSize;

            // Mouse wheel
            float scroll = Input.mouseScrollDelta.y;
            size -= scroll * (zoomSpeed * 0.02f);

            // Gamepad triggers (wrapper). Prefer explicit RT/LT; CombinedTriggers is a fallback.
            float rt = GamepadInput.RT;
            float lt = GamepadInput.LT;
            float triggerZoom = (rt - lt); // RT positive -> zoom in; LT positive -> zoom out
            if (Mathf.Abs(triggerZoom) < 0.001f)
                triggerZoom = GamepadInput.CombinedTriggers; // for legacy combined axis

            size -= triggerZoom * (zoomSpeed * Time.unscaledDeltaTime);
            mapCamera.orthographicSize = Mathf.Clamp(size, minOrthoSize, maxOrthoSize);
        }
    }

    // ---------- Right Stick Cursor + Click ----------
    private void HandleCursorMoveAndClick()
    {
        if (cursor == null || _canvasRect == null || !_mapOpen) return;

        // If we're not in gamepad modality, don't process fake-cursor input
        if (autoSwitchCursorByLastInput && !_usingGamepadNow) return;

        Vector2 rs = GamepadInput.RightStick;
        if (invertCursorY) rs.y = -rs.y;

        // RB speed boost
        float speed = cursorSpeed * (GamepadInput.RB ? rbSpeedBoost : 1f);

        // --- Flick detection state machine ---
        float mag = rs.magnitude;
        if (enableFlickSnap)
        {
            if (!_flickArmed)
            {
                if (mag >= flickMagnitudeThreshold)
                {
                    _flickArmed = true;
                    _flickTimer = 0f;
                }
            }
            else
            {
                _flickTimer += Time.unscaledDeltaTime;
                if (mag < 0.25f && _flickTimer <= flickTimeWindow)
                {
                    // Consider that a flick. Snap to nearest icon.
                    SnapCursorToNearestVisibleIcon();
                    _flickArmed = false;
                }
                else if (_flickTimer > flickTimeWindow)
                {
                    _flickArmed = false; // window expired
                }
            }
        }

        // Movement from stick
        if (mag > 0.0001f)
        {
            _cursorPos += rs * speed * Time.unscaledDeltaTime;
            if (diagLogs) Debug.Log($"[MapModeController] RightStick: {rs} (speed {speed:F1})");
        }
        else if (logZeroRightStick)
        {
            Debug.Log("[MapModeController] RightStick: (0,0) — if cursor doesn't move, verify Input Manager axes match GamepadInput constants.");
        }

        // Optional mouse delta assist (debugging) — ignored if not using gamepad modality
        if (enableMouseDeltaAssist)
        {
            var md = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
            if (md.sqrMagnitude > 0f)
                _cursorPos += md * (cursorSpeed * mouseAssistMultiplier) * Time.unscaledDeltaTime;
        }

        // Clamp to canvas
        Vector2 half = _canvasRect.rect.size * 0.5f;
        _cursorPos.x = Mathf.Clamp(_cursorPos.x, -half.x, half.x);
        _cursorPos.y = Mathf.Clamp(_cursorPos.y, -half.y, half.y);

        ApplyCursorPosition();

        // Click with A or Enter (Enter allowed while in pad modality for parity)
        bool clicked = GamepadInput.A_Pressed || Input.GetKeyDown(selectKey);
        if (clicked)
        {
            Vector2 screenPos = CanvasToScreenPoint(_cursorPos);
            DoUIRaycastClick(screenPos);
        }
    }

    private void SnapCursorToNearestVisibleIcon()
    {
        if (iconContainer == null || _icons.Count == 0 || cursor == null) return;

        RectTransform target = null;
        float bestDistSq = float.PositiveInfinity;

        foreach (var kv in _icons)
        {
            var runtime = kv.Value;
            if (runtime == null || runtime.rt == null) continue;
            if (!runtime.rt.gameObject.activeInHierarchy) continue;

            // Distance in canvas local space
            Vector2 d = runtime.rt.anchoredPosition - _cursorPos;
            float dsq = d.sqrMagnitude;
            if (dsq < bestDistSq)
            {
                bestDistSq = dsq;
                target = runtime.rt;
            }
        }

        if (target != null)
        {
            _cursorPos = target.anchoredPosition;
            ApplyCursorPosition();
            if (diagLogs) Debug.Log($"[MapModeController] Flick-snap to {target.gameObject.name}");
        }
    }

    // Hardened click path: uses canvas camera for screen conversion and bubbles to Button/IPointerClickHandler.
    private void DoUIRaycastClick(Vector2 screenPos)
    {
        if (_eventSystem == null)
            _eventSystem = EventSystem.current;
        if (_eventSystem == null) return;

        // Ensure we have a raycaster
        if (raycasters == null || raycasters.Count == 0)
        {
            if (mapCanvas != null)
            {
                var fallback = mapCanvas.GetComponent<GraphicRaycaster>();
                if (fallback != null)
                {
                    raycasters = new List<GraphicRaycaster> { fallback };
                }
            }
            if (raycasters == null || raycasters.Count == 0) return;
        }

        var ped = new PointerEventData(_eventSystem)
        {
            position = screenPos,
            button = PointerEventData.InputButton.Left,
            clickCount = 1
        };

        // Gather hits from all provided raycasters
        var hits = new List<RaycastResult>(16);
        foreach (var r in raycasters)
        {
            if (r == null) continue;
            r.Raycast(ped, hits);
        }

        if (hits.Count == 0)
        {
            if (diagLogs) Debug.Log("[MapModeController] Cursor click: no UI under cursor.");
            return;
        }

        // Choose top hit and bubble to a clickable target
        GameObject hitGO = hits[0].gameObject;
        var button = hitGO.GetComponentInParent<Button>(true);
        var clickHandler = hitGO.GetComponentInParent<IPointerClickHandler>(true);

        if (button != null && button.interactable && button.gameObject.activeInHierarchy)
        {
            if (diagLogs) Debug.Log($"[MapModeController] Invoking Button.onClick on {button.gameObject.name}");
            button.onClick.Invoke();
            _eventSystem.SetSelectedGameObject(button.gameObject);
            return;
        }

        if (clickHandler != null)
        {
            var target = (clickHandler as Component).gameObject;
            if (diagLogs) Debug.Log($"[MapModeController] Executing IPointerClickHandler on {target.name}");
            ExecuteEvents.Execute(target, ped, ExecuteEvents.pointerClickHandler);
            _eventSystem.SetSelectedGameObject(target);
            return;
        }

        // Fallback: fire a classic pointer sequence on the topmost hit
        if (diagLogs) Debug.Log($"[MapModeController] Fallback pointer sequence on {hitGO.name}");
        ExecuteEvents.Execute(hitGO, ped, ExecuteEvents.pointerEnterHandler);
        ExecuteEvents.Execute(hitGO, ped, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(hitGO, ped, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(hitGO, ped, ExecuteEvents.pointerExitHandler);
    }

    private bool IsPointerOverClickableUI()
    {
        if (EventSystem.current == null || mapCanvas == null) return false;
        var gr = mapCanvas.GetComponent<GraphicRaycaster>();
        if (gr == null) return false;

        var ped = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
        var results = new List<RaycastResult>();
        gr.Raycast(ped, results);

        for (int i = 0; i < results.Count; i++)
        {
            var go = results[i].gameObject;
            if (!go || !go.activeInHierarchy) continue;
            if (go.GetComponentInParent<Button>(true) != null) return true;
            if (go.GetComponentInParent<IPointerClickHandler>(true) != null) return true;
            if (go.GetComponentInParent<IDragHandler>(true) != null) return true;
        }
        return false;
    }

    private Vector3 ClampToBounds(Vector3 pos)
    {
        if (!clampToWorldBounds || worldBounds.size == Vector3.zero || mapCamera == null) return pos;

        float size = mapCamera.orthographicSize;
        float aspect = Mathf.Max(0.0001f, mapCamera.aspect);

        Vector3 f = mapCamera.transform.forward; f.y = 0f; if (f.sqrMagnitude < 1e-6f) f = Vector3.forward; f.Normalize();
        Vector3 r = mapCamera.transform.right; r.y = 0f; if (r.sqrMagnitude < 1e-6f) r = Vector3.right; r.Normalize();
        float halfWidth = size * aspect;
        float halfHeight = size;

        float ex = Mathf.Abs(r.x) * halfWidth + Mathf.Abs(f.x) * halfHeight;
        float ez = Mathf.Abs(r.z) * halfWidth + Mathf.Abs(f.z) * halfHeight;

        pos.x = Mathf.Clamp(pos.x, worldBounds.min.x + ex, worldBounds.max.x - ex);
        pos.z = Mathf.Clamp(pos.z, worldBounds.min.z + ez, worldBounds.max.z - ez);
        pos.y = mapCamera.transform.position.y;
        return pos;
    }

    private void UpdateIconScreenPositions()
    {
        if (!mapCamera || !_mapOpen) return;
        foreach (var kv in _icons)
        {
            var area = kv.Key;
            var runtime = kv.Value;
            if (!area || runtime.rt == null) continue;

            Vector3 screen = mapCamera.WorldToScreenPoint(area.iconWorldAnchor.position);
            bool visible = screen.z > 0f;
            runtime.rt.gameObject.SetActive(visible);
            if (!visible) continue;

            Vector2 localPoint;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)iconContainer, new Vector2(screen.x, screen.y), null, out localPoint);
            runtime.rt.anchoredPosition = localPoint;
        }
    }

    // ------------ Cursor helpers ------------
    private void ApplyCursorPosition()
    {
        if (cursor == null) return;
        cursor.anchoredPosition = _cursorPos;
    }

    /// <summary>
    /// Converts canvas anchored position to screen position.
    /// Uses the CANVAS camera for Screen Space - Camera / World Space canvases.
    /// </summary>
    private Vector2 CanvasToScreenPoint(Vector2 canvasAnchoredPos)
    {
        // Overlay: anchored (0,0) == screen center
        if (cursorCanvas == null || cursorCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            Vector2 half = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            return half + canvasAnchoredPos;
        }

        // Screen Space - Camera / World Space: convert canvas local -> world -> screen using CANVAS camera
        Vector2 canvasPos = canvasAnchoredPos + (_canvasRect.rect.size * 0.5f);
        Vector3 world = _canvasRect.TransformPoint(new Vector3(canvasPos.x, canvasPos.y, 0f));
        Camera uiCam = cursorCanvas.worldCamera != null ? cursorCanvas.worldCamera : Camera.main;
        return uiCam.WorldToScreenPoint(world);
    }

    // ------------ Input Modality (auto-switch) ------------
    private void UpdateInputActivityTracking()
    {
        float now = Time.unscaledTime;

        // Detect gamepad activity (using your GamepadInput wrapper + common joystick buttons)
        bool padActive = false;

        Vector2 ls = GamepadInput.LeftStick;
        Vector2 rs = GamepadInput.RightStick;
        float rt = GamepadInput.RT;
        float lt = GamepadInput.LT;

        if (ls.sqrMagnitude > 0.001f || rs.sqrMagnitude > 0.001f || Mathf.Abs(rt) > 0.001f || Mathf.Abs(lt) > 0.001f)
            padActive = true;

        if (GamepadInput.A_Pressed || GamepadInput.RB)
            padActive = true;

        // Old input system joystick buttons as a fallback
        if (!padActive)
        {
            for (int i = 0; i <= 19; i++)
            {
                if (Input.GetKeyDown(KeyCode.JoystickButton0 + i)) { padActive = true; break; }
            }
        }

#if ENABLE_INPUT_SYSTEM
        // New Input System quick presence check (doesn't force dependency)
        if (!padActive && Gamepad.current != null && Gamepad.current.wasUpdatedThisFrame)
            padActive = true;
#endif

        if (padActive) _lastGamepadActivityTime = now;

        // Detect mouse/keyboard activity
        bool mkActive = false;
        if (Input.anyKeyDown) mkActive = true;
        if (Mathf.Abs(Input.GetAxisRaw("Mouse X")) > 0.0f || Mathf.Abs(Input.GetAxisRaw("Mouse Y")) > 0.0f)
            mkActive = true;
        if (Input.mouseScrollDelta.sqrMagnitude > 0f) mkActive = true;

        if (mkActive) _lastMouseKeyboardActivityTime = now;

        // Decide modality
        if (autoSwitchCursorByLastInput)
        {
            bool gamepadRecent = (now - _lastGamepadActivityTime) <= inputActivityWindow;
            bool mkRecent = (now - _lastMouseKeyboardActivityTime) <= inputActivityWindow;

            // Prefer whichever is most recent; tie goes to mouse/keyboard (keeps OS cursor visible).
            if (gamepadRecent && (!mkRecent || _lastGamepadActivityTime >= _lastMouseKeyboardActivityTime))
                _usingGamepadNow = true;
            else
                _usingGamepadNow = false;
        }
        else
        {
            // If autoswitch is disabled, respect hideSystemCursorInMapMode as a coarse toggle for OS cursor vs fake cursor
            _usingGamepadNow = hideSystemCursorInMapMode;
        }
    }

    private void UpdateCursorVisibilityForModality()
    {
        if (!_mapOpen) return;

        if (_usingGamepadNow)
        {
            // Show fake cursor, hide OS cursor if requested
            if (cursor != null && !cursor.gameObject.activeSelf) cursor.gameObject.SetActive(true);
            Cursor.visible = !hideSystemCursorInMapMode ? true : false;
        }
        else
        {
            // Hide fake cursor, show OS cursor
            if (cursor != null && cursor.gameObject.activeSelf) cursor.gameObject.SetActive(false);
            Cursor.visible = true;
        }
    }

    // -------- Version-safe helper to find GameManager without obsolete API --------
    private static GameManager FindGameManagerCompat()
    {
#if UNITY_2023_1_OR_NEWER
        // If you want to include inactive, use the overload with FindObjectsInactive.Include
        return UnityEngine.Object.FindFirstObjectByType<GameManager>();
#elif UNITY_2022_2_OR_NEWER
        return UnityEngine.Object.FindFirstObjectByType<GameManager>();
#else
        return UnityEngine.Object.FindObjectOfType<GameManager>();
#endif
    }
}

/// Minimal adapter to your real GoalPointer
public class GoalPointerAdapter : MonoBehaviour
{
    public enum GoalCategory { Boss, Helicopter, Friendly, Shop, Other }

    public void SetGoal(Transform target, MapKeyArea.AreaType type)
    {
        if (GoalPointer.Instance != null)
            GoalPointer.Instance.SelectFromMap(target, type);
        else
            Debug.Log("[GoalPointerAdapter] No GoalPointer.Instance; selection logged only.");

        Debug.Log("[GoalPointerAdapter] SetGoal to " + (target ? target.name : "null") + " (" + type + ").");
    }

    public static GoalCategory Convert(MapKeyArea.AreaType t)
    {
        switch (t)
        {
            case MapKeyArea.AreaType.Boss: return GoalCategory.Boss;
            case MapKeyArea.AreaType.Helicopter: return GoalCategory.Helicopter;
            case MapKeyArea.AreaType.FriendlySpawn: return GoalCategory.Friendly;
            case MapKeyArea.AreaType.Shop: return GoalCategory.Shop;
            default: return GoalCategory.Other;
        }
    }
}
