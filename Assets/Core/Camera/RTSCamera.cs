using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class RTSCamera : MonoBehaviour
{
    [Header("References")]
    public Transform pivot;  
    public Camera cam;        

    [Header("Pan / Translate")]
    public float moveSpeed = 24f;
    public bool enableEdgeScroll = true;
    public float edgeSize = 14f;
    public float edgeBoost = 1.6f;
    public Vector2 xzLimits = new Vector2(250, 250); 

    [Header("MMB Drag Pan")]
    public float dragPanFactor = 0.05f;
    public bool invertDragX = true;
    public bool invertDragY = true;

    [Header("Rotation (RMB drag yaw only)")]
    public float rotateSpeed = 2.0f;

    [Header("Zoom Rig (distance + tilt driven by zoom)")]
    [Tooltip("Normalized zoom 0=close/shallower, 1=far/top-down")]
    [Range(0, 1)] public float zoomT = 0.35f;
    public float zoomSpeed = 1.8f;  
    public bool invertZoom = false; 

    [Tooltip("Close view settings (at zoomT = 0)")]
    public float closeHeight = 18f;
    public float closeDistance = 18f;  
    [Range(0, 89)] public float closeTilt = 45f;

    [Tooltip("Far view settings (at zoomT = 1)")]
    public float farHeight = 85f;
    public float farDistance = 0f;    
    [Range(0, 89)] public float farTilt = 85f; 

    [Header("Zoom Curves (remap zoomT 0..1 ? 0..1)")]
    [Tooltip("Shape how quickly tilt approaches top-down. Make this steep near 1 for 'all at once'.")]
    public AnimationCurve tiltCurve = AnimationCurve.Linear(0, 0, 1, 1);
    [Tooltip("Shape height growth vs zoom. Often gentle early, faster late.")]
    public AnimationCurve heightCurve = AnimationCurve.Linear(0, 0, 1, 1);
    [Tooltip("Shape distance pull-in/out vs zoom. If farDistance=0, this can stay linear.")]
    public AnimationCurve distCurve = AnimationCurve.Linear(0, 0, 1, 1);

    [Header("World Bounds Lock")]
    public bool lockWithinBounds = false;
    [Tooltip("Assign a TerrainCollider, MeshCollider, or BoxCollider that encloses the playable area.")]
    public Collider worldBoundsCollider;
    [Tooltip("Inset so the camera never reveals beyond the edge.")]
    public float boundsPadding = 8f;

    [Header("Terrain Clamp (optional, vertical)")]
    public bool clampToTerrain = false;
    public LayerMask groundMask = ~0;
    public float cameraHeightOffset = 0f;

    // --- Input System ---
    private InputSystem_Actions controls; 
    private Vector2 moveInput;              
    private Vector2 rotateInput;             
    private float zoomInput;                 

    void Awake()
    {
        controls = new InputSystem_Actions();
        if (!pivot) pivot = transform;
        if (!cam) cam = GetComponentInChildren<Camera>();
        ApplyZoomRig(); 
    }

    void OnEnable()
    {
        controls.Camera.Enable();

        controls.Camera.Move.performed += ctx => moveInput = ctx.ReadValue<Vector2>();
        controls.Camera.Move.canceled += _ => moveInput = Vector2.zero;

        controls.Camera.Rotate.performed += ctx => rotateInput = ctx.ReadValue<Vector2>();
        controls.Camera.Rotate.canceled += _ => rotateInput = Vector2.zero;

        controls.Camera.Zoom.performed += ctx => zoomInput = ctx.ReadValue<float>();
        controls.Camera.Zoom.canceled += _ => zoomInput = 0f;
    }

    void OnDisable()
    {
        controls.Camera.Disable();
    }

    void Update()
    {
        Vector3 worldMove = ComputePanFromWASD()
                          + ComputePanFromEdgeScroll()
                          + ComputePanFromMMBDrag_Direct();

        if (worldMove.sqrMagnitude > 0f)
        {
            pivot.position += worldMove * Time.deltaTime;
            ClampPivotToBounds(); 
        }

        ApplyRotationYawFromRMB();
        ApplyZoomWithTilt();

        if (clampToTerrain) ClampToGround();
    }

    Vector3 ComputePanFromWASD()
    {
        if (moveInput.sqrMagnitude < 0.0001f) return Vector3.zero;
        Vector3 dir = new Vector3(moveInput.x, 0f, moveInput.y);
        dir = Quaternion.Euler(0f, pivot.eulerAngles.y, 0f) * dir;
        return dir.normalized * moveSpeed;
    }

    Vector3 ComputePanFromEdgeScroll()
    {
        if (!enableEdgeScroll || !Application.isFocused || Cursor.lockState != CursorLockMode.None) return Vector3.zero;
        if (EventSystem.current && EventSystem.current.IsPointerOverGameObject()) return Vector3.zero;

        var mouse = Mouse.current; if (mouse == null) return Vector3.zero;
        Vector2 m = mouse.position.ReadValue();
        if (m.x < 0 || m.y < 0 || m.x > Screen.width || m.y > Screen.height) return Vector3.zero;

        float ex = 0f, ez = 0f;
        if (m.x <= edgeSize) { float t = 1f - Mathf.Clamp01(m.x / edgeSize); ex = -Mathf.Lerp(0.5f, edgeBoost, t); }
        else if (m.x >= Screen.width - edgeSize) { float t = 1f - Mathf.Clamp01((Screen.width - m.x) / edgeSize); ex = Mathf.Lerp(0.5f, edgeBoost, t); }
        if (m.y <= edgeSize) { float t = 1f - Mathf.Clamp01(m.y / edgeSize); ez = -Mathf.Lerp(0.5f, edgeBoost, t); }
        else if (m.y >= Screen.height - edgeSize) { float t = 1f - Mathf.Clamp01((Screen.height - m.y) / edgeSize); ez = Mathf.Lerp(0.5f, edgeBoost, t); }

        if (ex == 0f && ez == 0f) return Vector3.zero;

        Vector3 dir = new Vector3(ex, 0f, ez);
        dir = Quaternion.Euler(0f, pivot.eulerAngles.y, 0f) * dir;
        return dir.normalized * moveSpeed;
    }

    Vector3 ComputePanFromMMBDrag_Direct()
    {
        var mouse = Mouse.current;
        if (mouse == null || !mouse.middleButton.isPressed) return Vector3.zero;

        Vector2 delta = mouse.delta.ReadValue();
        if (delta.sqrMagnitude < 0.0001f) return Vector3.zero;

        float sx = (invertDragX ? -1f : 1f);
        float sy = (invertDragY ? -1f : 1f);

        float height = cam.transform.localPosition.y;
        float scale = Mathf.Max(0.01f, height / Mathf.Max(0.01f, closeHeight));

        Vector3 drag = new Vector3(delta.x * sx, 0f, delta.y * sy)
                       * (moveSpeed * dragPanFactor * scale);

        drag = Quaternion.Euler(0f, pivot.eulerAngles.y, 0f) * drag;
        return drag;
    }

    void ApplyRotationYawFromRMB()
    {
        var mouse = Mouse.current;
        if (mouse == null || !mouse.rightButton.isPressed) return;
        if (rotateInput.sqrMagnitude < 0.0001f) return;
        pivot.Rotate(Vector3.up, rotateInput.x * rotateSpeed, Space.World); 
    }

    void ApplyZoomWithTilt()
    {
        if (Mathf.Abs(zoomInput) > 0.0001f)
        {
            float dir = invertZoom ? -1f : 1f;
            zoomT = Mathf.Clamp01(zoomT - dir * zoomInput * (zoomSpeed * Time.deltaTime));
        }
        ApplyZoomRig();
    }

    void ApplyZoomRig()
    {
        if (!cam) return;

        float tTilt = Mathf.Clamp01(tiltCurve.Evaluate(zoomT));
        float tHeight = Mathf.Clamp01(heightCurve.Evaluate(zoomT));
        float tDist = Mathf.Clamp01(distCurve.Evaluate(zoomT));

        float h = Mathf.Lerp(closeHeight, farHeight, tHeight);
        float d = Mathf.Lerp(closeDistance, farDistance, tDist);
        float tilt = Mathf.Lerp(closeTilt, farTilt, tTilt);

        cam.transform.localPosition = new Vector3(0f, h, -d);

        Vector3 e = cam.transform.localEulerAngles;
        e.x = tilt; e.y = 0f; e.z = 0f;
        cam.transform.localEulerAngles = e;
    }

    // --- Helpers ---
    void ClampPivotToBounds()
    {
        if (lockWithinBounds && worldBoundsCollider)
        {
            Bounds b = worldBoundsCollider.bounds;

            float pad = Mathf.Max(0f, boundsPadding);
            float minX = b.min.x + pad;
            float maxX = b.max.x - pad;
            float minZ = b.min.z + pad;
            float maxZ = b.max.z - pad;

            Vector3 p = pivot.position;
            p.x = Mathf.Clamp(p.x, minX, maxX);
            p.z = Mathf.Clamp(p.z, minZ, maxZ);
            pivot.position = p;
        }
        else
        {
            pivot.position = new Vector3(
                Mathf.Clamp(pivot.position.x, -xzLimits.x, xzLimits.x),
                pivot.position.y,
                Mathf.Clamp(pivot.position.z, -xzLimits.y, xzLimits.y)
            );
        }
    }

    void ClampToGround()
    {
        Vector3 from = pivot.position + Vector3.up * 500f;
        if (Physics.Raycast(from, Vector3.down, out var hit, 1000f, groundMask, QueryTriggerInteraction.Ignore))
        {
            pivot.position = new Vector3(pivot.position.x, hit.point.y + cameraHeightOffset, pivot.position.z);
        }
    }
}
