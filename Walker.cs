using System;
using System.Collections.Generic;
using Il2CppPlayer.Appearances.God;
using Il2CppPlayer.Control.Service;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;


namespace PhxCore
{
    sealed class HeldAnchor
    {
        public Transform Target;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
    }

    /// <summary>
    /// Walking physics, ported from the Walking mod. The player gets a capsule, gravity, sprint and jump while the game's
    /// noclip flight is off. Movement (below) decides when noclip is on.
    /// </summary>
    internal sealed class Walker
    {
        const float BodyRadius = 0.35f;
        const float BodyHeight = 1.7f;
        const float WalkSpeed = 6.5f;
        const float SprintSpeed = 12f;
        const float JumpHeight = 1.8f;
        const float GroundAccelTime = 0.12f;
        const float GroundStopTime = 0.09f;
        const float AirAccelTime = 0.22f;
        const float AirStopTime = 0.4f;
        const float Skin = 0.03f;
        const float SnapDistance = 0.25f;
        const float StepHeight = 0.45f;
        const float MaxSlopeDegrees = 55f;
        const float MaxFrameStep = 0.016f;

        GAReferences _player;
        Rigidbody _body;
        CapsuleCollider _capsule;
        RigidbodyConstraints _savedConstraints;
        bool _hasSavedConstraints;
        bool _wasNoclip = true;
        bool _built;
        float _rebuildAt;
        float _floorY;
        float _jumpBufferUntil;
        float _lastGroundedTime;
        bool _spaceHeld;
        Vector3 _pivotOffset;
        bool _hasPivotOffset;
        float _savedDamping;
        bool _hasSavedDamping;
        float _savedDepenetration;
        bool _hasSavedDepenetration;
        RigidbodyInterpolation _savedInterpolation;
        bool _hasSavedInterpolation;
        bool _savedKinematic;
        bool _hasSavedKinematic;
        bool _savedGravity;
        bool _hasSavedGravity;
        bool _hasFlightState;
        bool _flightKinematic;
        bool _flightGravity;
        bool _flightDetect;
        RigidbodyConstraints _flightConstraints;
        float _flightDamping;
        float _flightDepenetration;
        RigidbodyInterpolation _flightInterpolation;
        CollisionDetectionMode _flightCollisionMode;
        PhysicsMaterial _slide;
        float _unstickUntil;
        Vector3 _planar;
        float _vertical;
        Vector3 _simPosition;
        bool _hasVisual;
        bool _simulating;
        Vector3 _eyeOffset;
        bool _hasEyeOffset;
        int _followFrame = -1;
        Camera.CameraCallback _preCull;
        readonly List<HeldAnchor> _held = new List<HeldAnchor>();

        Hotkey _noclipKey;
        bool _stepGrounded;
        static readonly float MinWalkNormal = Mathf.Cos(MaxSlopeDegrees * Mathf.Deg2Rad);
        Vector3 _spawn;
        bool _hasSpawn;
        float _startAt;
        bool _startApplied;
        int _pending = -1;

        internal void Init(Hotkey noclipKey)
        {
            _noclipKey = noclipKey;
            EnsureCameraHook();
        }

        internal void Shutdown()
        {
            if (_preCull != null)
            {
                Camera.onPreCull -= _preCull;
                _preCull = null;
            }
            ClearGround();
            if (_body != null && (_hasSavedKinematic || _hasSavedGravity || _hasSavedConstraints))
                ReleaseToFlight();
            else if (_capsule != null)
                _capsule.enabled = false;
        }

        internal void SceneLoaded()
        {
            ClearGround();
            _player = null;
            _body = null;
            _capsule = null;
            _hasSavedConstraints = false;
            _wasNoclip = true;
            _built = false;
            _rebuildAt = 0f;
            _jumpBufferUntil = 0f;
            _lastGroundedTime = 0f;
            _spaceHeld = false;
            _hasPivotOffset = false;
            _hasSavedDamping = false;
            _hasSavedDepenetration = false;
            _hasSavedInterpolation = false;
            _hasSavedKinematic = false;
            _hasSavedGravity = false;
            _hasFlightState = false;
            _unstickUntil = 0f;
            _planar = Vector3.zero;
            _vertical = 0f;
            _hasVisual = false;
            _simulating = false;
            _hasEyeOffset = false;
            _followFrame = -1;
            if (_slide != null)
            {
                Object.Destroy(_slide);
                _slide = null;
            }
            _held.Clear();
            _hasSpawn = false;
            _startAt = 0f;
            _startApplied = false;
            _pending = -1;
        }

        internal void Update()
        {
            bool paused = Menu.IsPaused();

            if (_player == null)
                _player = Object.FindFirstObjectByType<GAReferences>();
            if (_player == null || _player.BodyPhysics == null || _player.BodyPhysics.m_rb == null)
                return;

            _body = _player.BodyPhysics.m_rb;
            if (!_hasSpawn)
            {
                _spawn = _body.position;
                _hasSpawn = true;
                _startAt = Time.unscaledTime + 0.5f;
            }
            if (!_startApplied && Time.unscaledTime >= _startAt)
            {
                _startApplied = true;
                SetWalking(Movement.WalkByDefault);
            }
            if (_pending >= 0)
            {
                SetWalking(_pending == 1);
                _pending = -1;
            }
            if (IsNoclip() && !_hasFlightState && !_hasSavedKinematic)
                RememberFlightBody();
            if (!IsNoclip())
                EnsureCapsule();

            if (!_built)
            {
                BuildGround();
                _built = true;
                _rebuildAt = Time.unscaledTime + 2f;
            }
            else if (_rebuildAt > 0f && Time.unscaledTime >= _rebuildAt)
            {
                BuildGround();
                _rebuildAt = 0f;
            }

            SyncBody();
            if (!IsNoclip() && !Player.Frozen)
            {
                PollJump();
                Walk();
            }

            if (!paused)
                PollNoclipHotkey();
        }

        internal void FixedUpdate()
        {
            if (_capsule == null)
                return;

            SyncBody();
        }

        internal void LateUpdate()
        {
            if (_body == null || IsNoclip())
                return;
            FollowWithCamera();
            FollowHeld();
        }

        void EnsureCapsule()
        {
            _capsule = _body.GetComponent<CapsuleCollider>();
            if (_capsule == null)
                _capsule = _body.gameObject.AddComponent<CapsuleCollider>();

            _capsule.direction = 1;
            _capsule.radius = BodyRadius;
            _capsule.height = BodyHeight;
            _capsule.isTrigger = false;
            AlignCapsuleToCamera();

            _body.detectCollisions = true;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        void AlignCapsuleToCamera()
        {
            float centerY = 0f;
            if (_player.CameraService != null)
            {
                float eyeAboveBody = _player.CameraService.CameraPosition.y - _body.position.y;
                if (eyeAboveBody < 0.35f)
                    centerY = -BodyHeight * 0.5f;
            }
            _capsule.center = new Vector3(0f, centerY, 0f);
        }

        void SyncBody()
        {
            if (_capsule == null || _body == null || _player == null)
                return;

            if (IsNoclip())
            {
                if (_capsule != null)
                    _capsule.enabled = false;
                if (_hasSavedKinematic || _hasSavedGravity || _hasSavedConstraints)
                    ReleaseToFlight();
                return;
            }

            if (_capsule != null)
                _capsule.enabled = true;

            if (!_hasSavedConstraints)
                {
                    _savedConstraints = _body.constraints;
                    _hasSavedConstraints = true;
                    _body.constraints = _savedConstraints | RigidbodyConstraints.FreezeRotation;
                }
                if (!_hasSavedDamping)
                {
                    _savedDamping = _body.linearDamping;
                    _hasSavedDamping = true;
                    _body.linearDamping = 0f;
                }
                if (!_hasSavedDepenetration)
                {
                    _savedDepenetration = _body.maxDepenetrationVelocity;
                    _hasSavedDepenetration = true;
                    _body.maxDepenetrationVelocity = 1.5f;
                }
                if (!_hasSavedInterpolation)
                {
                    _savedInterpolation = _body.interpolation;
                    _hasSavedInterpolation = true;
                }
                _body.interpolation = RigidbodyInterpolation.None;
                if (!_hasSavedGravity)
                {
                    _savedGravity = _body.useGravity;
                    _hasSavedGravity = true;
                }
                _body.useGravity = false;
                if (!_hasSavedKinematic)
                {
                    _savedKinematic = _body.isKinematic;
                    _hasSavedKinematic = true;
                }
                _body.isKinematic = true;
                EnsureSlideMaterial();
                if (!_hasPivotOffset)
                {
                    CapturePivotOffset();
                    CaptureHeld();
                }

                if (_wasNoclip)
                {
                    _planar = Vector3.zero;
                    _vertical = 0f;
                    _simPosition = _body.transform.position;
                    _hasVisual = true;
                    LiftOntoFloor();
                }

            _wasNoclip = false;
        }

        void RememberFlightBody()
        {
            if (_body == null || _hasFlightState)
                return;
            _flightKinematic = _body.isKinematic;
            _flightGravity = _body.useGravity;
            _flightDetect = _body.detectCollisions;
            _flightConstraints = _body.constraints;
            _flightDamping = _body.linearDamping;
            _flightDepenetration = _body.maxDepenetrationVelocity;
            _flightInterpolation = _body.interpolation;
            _flightCollisionMode = _body.collisionDetectionMode;
            _hasFlightState = true;
        }

        void ReleaseToFlight()
        {
            if (_body == null)
                return;

            if (_hasVisual)
            {
                _body.position = _simPosition;
                _body.transform.position = _simPosition;
            }

            if (_capsule != null)
                _capsule.enabled = false;

            if (_hasFlightState)
            {
                _body.constraints = _flightConstraints;
                _body.linearDamping = _flightDamping;
                _body.maxDepenetrationVelocity = _flightDepenetration;
                _body.interpolation = _flightInterpolation;
                _body.collisionDetectionMode = _flightCollisionMode;
                _body.detectCollisions = _flightDetect;
                _body.useGravity = _flightGravity;
                _body.isKinematic = _flightKinematic;
            }
            else
            {
                _body.useGravity = false;
                _body.isKinematic = true;
            }

            if (!_body.isKinematic)
            {
                _body.velocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }

            _hasSavedConstraints = false;
            _hasSavedDamping = false;
            _hasSavedDepenetration = false;
            _hasSavedInterpolation = false;
            _hasSavedGravity = false;
            _hasSavedKinematic = false;
            _hasPivotOffset = false;
            _hasEyeOffset = false;
            _hasVisual = false;
            _planar = Vector3.zero;
            _vertical = 0f;
            _held.Clear();
            _wasNoclip = true;
            Physics.SyncTransforms();
        }

        bool IsNoclip()
        {
            return _player != null && _player.NoClipMoving != null && _player.NoClipMoving.m_activated;
        }

        void PollNoclipHotkey()
        {
            if (_noclipKey == null || !_noclipKey.Pressed())
                return;
            SetWalking(IsNoclip());
        }

        internal bool Walking => _player != null && _player.NoClipMoving != null && !_player.NoClipMoving.m_activated;

        /// <summary>Asks for walking or noclip. Applied on the next frame the player exists.</summary>
        internal void Request(bool walking)
        {
            _pending = walking ? 1 : 0;
        }

        internal void SetWalking(bool walking)
        {
            if (_player == null || _player.NoClipMoving == null)
                return;
            if (walking == !IsNoclip())
                return;

            if (walking)
            {
                if (!_hasFlightState)
                    RememberFlightBody();
                _player.NoClipMoving.Deactivate();
                return;
            }

            ReleaseToFlight();
            Vector3 here = _body != null ? _body.position : _simPosition;
            var flight = _player.NoClipMoving;
            if (flight.m_noClipMovingDynamicData != null)
                flight.m_noClipMovingDynamicData.SetTargetBodyPosition(here);
            flight.m_velocity = Vector3.zero;
            flight.Activate();
            flight.Teleport(here);
        }

        /// <summary>Puts the player back where they first appeared in this scene, in the default movement mode.</summary>
        internal void Respawn()
        {
            if (_player == null || _body == null || !_hasSpawn)
            {
                MelonLogger.Warning("Nothing to respawn yet.");
                return;
            }

            if (IsNoclip())
            {
                var flight = _player.NoClipMoving;
                if (flight.m_noClipMovingDynamicData != null)
                    flight.m_noClipMovingDynamicData.SetTargetBodyPosition(_spawn);
                flight.m_velocity = Vector3.zero;
                flight.Teleport(_spawn);
            }
            else
            {
                _planar = Vector3.zero;
                _vertical = 0f;
                _simPosition = _spawn;
                _hasVisual = true;
                ApplyPose();
                if (_capsule != null)
                    LiftOntoFloor();
            }
            _pending = Movement.WalkByDefault ? 1 : 0;
        }

        void PollJump()
        {
            Keyboard keyboard = Keyboard.current;
            bool down = keyboard != null && keyboard.spaceKey.isPressed;
            if (down && !_spaceHeld)
                _jumpBufferUntil = Time.time + 0.25f;
            _spaceHeld = down;
        }

        void Walk()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || _capsule == null)
                return;

            if (!_hasVisual)
            {
                _simPosition = _body.transform.position;
                _hasVisual = true;
            }

            _capsule.enabled = false;
            _simulating = true;
            try
            {
                int steps = Mathf.Clamp(Mathf.CeilToInt(dt / MaxFrameStep), 1, 4);
                float stepDt = dt / steps;
                for (int i = 0; i < steps; i++)
                    WalkStep(stepDt);
                ApplyPose();
            }
            finally
            {
                _simulating = false;
                if (!IsNoclip())
                    _capsule.enabled = true;
            }
        }

        void WalkStep(float dt)
        {
            Vector2 axis = ReadMoveAxis();
            if (axis.sqrMagnitude > 1f)
                axis.Normalize();

            Vector3 forward = FlatForward();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 wish = forward * axis.y + right * axis.x;

            bool grounded = IsGrounded();
            if (grounded)
                _lastGroundedTime = Time.time;
            _stepGrounded = grounded && _vertical <= 0.01f;

            float speed = IsSprinting() ? SprintSpeed : WalkSpeed;
            bool moving = wish.sqrMagnitude > 0.0001f;
            float smoothTime = moving
                ? (grounded ? GroundAccelTime : AirAccelTime)
                : (grounded ? GroundStopTime : AirStopTime);
            float step = speed / smoothTime * dt;
            Vector3 target = moving ? wish * speed : Vector3.zero;
            _planar = Vector3.MoveTowards(_planar, target, step);

            if (Time.time <= _jumpBufferUntil && Time.time - _lastGroundedTime <= 0.15f)
            {
                _vertical = JumpVelocity();
                _jumpBufferUntil = 0f;
                _lastGroundedTime = 0f;
                _unstickUntil = Time.time + 0.12f;
            }
            else
            {
                float gravity = Physics.gravity.y;
                if (Mathf.Abs(gravity) < 1f)
                    gravity = -20f;
                _vertical += gravity * dt;
                if (_vertical < -40f)
                    _vertical = -40f;
            }

            _simPosition = SweepHorizontal(_simPosition, new Vector3(_planar.x, 0f, _planar.z) * dt);
            _simPosition = SweepVertical(_simPosition, _vertical * dt);
            if (Time.time >= _unstickUntil && _vertical <= 0.01f)
                _simPosition = SnapToGround(_simPosition);
        }

        Vector3 SweepHorizontal(Vector3 position, Vector3 motion)
        {
            for (int i = 0; i < 3; i++)
            {
                float remaining = motion.magnitude;
                if (remaining < 0.00001f)
                    break;

                Vector3 direction = motion / remaining;
                if (!TryCast(position, direction, remaining + Skin, out RaycastHit hit, true))
                {
                    position += motion;
                    break;
                }

                float travel = Mathf.Max(0f, hit.distance - Skin);
                position += direction * travel;
                Vector3 leftover = direction * Mathf.Max(0f, remaining - travel);
                motion = Vector3.ProjectOnPlane(leftover, hit.normal);
                motion.y = 0f;
                Vector3 slide = Vector3.ProjectOnPlane(new Vector3(_planar.x, 0f, _planar.z), hit.normal);
                _planar = new Vector3(slide.x, 0f, slide.z);
            }
            return position;
        }

        Vector3 SweepVertical(Vector3 position, float distance)
        {
            if (Mathf.Abs(distance) < 0.00001f)
                return position;

            Vector3 direction = distance > 0f ? Vector3.up : Vector3.down;
            float remaining = Mathf.Abs(distance);
            if (!TryCast(position, direction, remaining + Skin, out RaycastHit hit, false))
                return position + Vector3.up * distance;

            float travel = Mathf.Max(0f, hit.distance - Skin);
            if (distance < 0f || hit.normal.y < 0.2f)
                _vertical = 0f;
            return position + direction * travel;
        }

        /// <summary>
        /// Keeps the feet on the ground: pulls down onto surfaces just below (walking down slopes) and lifts up onto
        /// walkable surfaces the feet have sunk into (walking up slopes, stairs and small ledges up to StepHeight).
        /// </summary>
        Vector3 SnapToGround(Vector3 position)
        {
            Vector3 feet = Feet(position);
            Vector3 origin = feet + Vector3.up * StepHeight;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, StepHeight + SnapDistance, Physics.DefaultRaycastLayers))
                return position;
            if (!IsGroundHit(hit.collider) || hit.normal.y < MinWalkNormal)
                return position;

            float drop = feet.y - (hit.point.y + 0.02f);
            if (drop > SnapDistance)
                return position;

            if (drop < 0f)
            {
                if (!_stepGrounded && drop < -0.05f)
                    return position;
                if (HeadBlocked(position, -drop))
                    return position;
            }

            position.y -= drop;
            _vertical = 0f;
            return position;
        }

        bool HeadBlocked(Vector3 position, float lift)
        {
            CapsuleSegment(position, out _, out Vector3 top, out float radius);
            if (!Physics.Raycast(top, Vector3.up, out RaycastHit hit, radius + lift + Skin, Physics.DefaultRaycastLayers))
                return false;
            return IsGroundHit(hit.collider);
        }

        bool TryCast(Vector3 position, Vector3 direction, float distance, out RaycastHit closest, bool horizontal)
        {
            closest = default;
            if (distance <= 0.00001f || direction.sqrMagnitude < 0.00001f)
                return false;

            CapsuleSegment(position, out Vector3 bottom, out Vector3 top, out float radius);
            Vector3 dir = direction.normalized;
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 0.0001f)
                side = Vector3.Cross(dir, Vector3.forward);
            side.Normalize();
            Vector3 mid = (bottom + top) * 0.5f;
            float feetY = bottom.y - radius;
            Vector3 knee = new Vector3(bottom.x, feetY + StepHeight + 0.05f, bottom.z);

            float best = float.MaxValue;
            bool found = false;
            RaycastHit chosen = default;
            Consider(bottom);
            Consider(top);
            Consider(mid);
            Consider(mid + side * radius);
            Consider(mid - side * radius);
            Consider(bottom + side * radius);
            Consider(bottom - side * radius);
            if (horizontal && knee.y < mid.y)
            {
                Consider(knee);
                Consider(knee + side * radius);
                Consider(knee - side * radius);
            }
            closest = chosen;
            return found;

            void Consider(Vector3 origin)
            {
                Vector3 start = origin - dir * 0.02f;
                if (!Physics.Raycast(start, dir, out RaycastHit hit, distance + 0.02f, Physics.DefaultRaycastLayers))
                    return;
                if (!IsGroundHit(hit.collider) || hit.distance < 0f || hit.distance >= best)
                    return;
                if (Vector3.Dot(dir, hit.normal) > 0.05f)
                    return;
                if (horizontal)
                {
                    // Ramps you can walk up never block; SnapToGround lifts you onto them.
                    if (hit.normal.y >= MinWalkNormal)
                        return;
                    // While on the ground, edges low enough to step over do not block either.
                    if (_stepGrounded && hit.point.y - feetY <= StepHeight)
                        return;
                }
                best = hit.distance;
                chosen = hit;
                found = true;
            }
        }

        void CapsuleSegment(Vector3 position, out Vector3 bottom, out Vector3 top, out float radius)
        {
            radius = Mathf.Max(0.05f, _capsule.radius - 0.01f);
            Vector3 center = position + _capsule.center;
            float half = (_capsule.height * 0.5f) - radius;
            if (half < 0.01f)
                half = 0.01f;
            bottom = center - Vector3.up * half;
            top = center + Vector3.up * half;
        }

        Vector3 Feet(Vector3 position)
        {
            return position + _capsule.center - Vector3.up * (_capsule.height * 0.5f);
        }

        void ApplyPose()
        {
            _body.transform.position = _simPosition;
            Physics.SyncTransforms();
        }

        void EnsureCameraHook()
        {
            if (_preCull != null)
                return;
            try
            {
                _preCull = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Camera.CameraCallback>((Action<Camera>)OnCameraPreCull);
                Camera.onPreCull += _preCull;
            }
            catch (Exception e)
            {
                _preCull = null;
                MelonLogger.Warning("Could not hook the camera: " + e.Message);
            }
        }

        void OnCameraPreCull(Camera cam)
        {
            if (cam == null || _body == null || !_hasVisual || IsNoclip())
                return;
            if (_followFrame == Time.frameCount)
                return;
            _followFrame = Time.frameCount;
            ApplyPose();
            FollowWithCamera();
            FollowHeld();
        }

        void EnsureSlideMaterial()
        {
            if (_capsule == null)
                return;
            if (_slide != null)
                return;
            _slide = _capsule.material;
            if (_slide == null)
                return;
            _slide.bounciness = 0f;
            _slide.dynamicFriction = 0f;
            _slide.staticFriction = 0f;
            _slide.frictionCombine = PhysicsMaterialCombine.Minimum;
            _slide.bounceCombine = PhysicsMaterialCombine.Minimum;
        }

        float JumpVelocity()
        {
            float gravity = Mathf.Abs(Physics.gravity.y);
            if (gravity < 1f)
                gravity = 20f;
            return Mathf.Sqrt(2f * JumpHeight * gravity);
        }

        Vector2 ReadMoveAxis()
        {
            Vector2 axis = Vector2.zero;
            PlayerControlService control = _player.m_playerControlService == null
                ? null
                : _player.m_playerControlService.TryCast<PlayerControlService>();
            if (control != null && control.m_moveDataReader != null && control.m_moveDataReader.m_moveInputService != null)
                axis = control.m_moveDataReader.m_moveInputService.HorizontalMoveAxis;

            if (axis.sqrMagnitude > 0.01f)
                return axis;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return axis;

            float x = 0f;
            float y = 0f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
                x += 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
                x -= 1f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
                y += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
                y -= 1f;
            return new Vector2(x, y);
        }

        bool IsSprinting()
        {
            PlayerControlService control = _player.m_playerControlService == null
                ? null
                : _player.m_playerControlService.TryCast<PlayerControlService>();
            if (control != null && control.SpeedBoost > 1.2f)
                return true;
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        }

        Vector3 FlatForward()
        {
            Vector3 forward = Vector3.forward;
            if (_player.CameraService != null)
                forward = _player.CameraService.CameraRotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                return Vector3.forward;
            return forward.normalized;
        }

        bool IsGrounded()
        {
            if (_capsule == null)
                return false;

            Vector3 origin = _simulating ? _simPosition : _body.transform.position;
            Vector3 feet = origin + _capsule.center - Vector3.up * (_capsule.height * 0.5f);
            if (!Physics.Raycast(feet + Vector3.up * 0.25f, Vector3.down, out RaycastHit hit, 0.6f, Physics.DefaultRaycastLayers))
                return false;
            return IsGroundHit(hit.collider);
        }

        bool IsGroundHit(Collider hit)
        {
            if (hit == null || hit == _capsule || hit.isTrigger)
                return false;
            return hit.attachedRigidbody != _body;
        }

        void CapturePivotOffset()
        {
            Transform pivot = Pivot();
            if (pivot == null || pivot == _body.transform || pivot.IsChildOf(_body.transform))
            {
                _pivotOffset = Vector3.zero;
                _hasPivotOffset = true;
                return;
            }
            _pivotOffset = pivot.position - BodyVisual();
            Transform view = ViewTransform();
            if (view != null && view != pivot && !view.IsChildOf(pivot))
            {
                _eyeOffset = view.position - pivot.position;
                _hasEyeOffset = true;
            }
            _hasPivotOffset = true;
        }

        Vector3 BodyVisual()
        {
            if (_hasVisual)
                return _simPosition;
            return _body.transform.position;
        }

        void FollowWithCamera()
        {
            Transform pivot = Pivot();
            if (pivot == null || pivot == _body.transform || pivot.IsChildOf(_body.transform))
                return;

            Vector3 target = BodyVisual() + _pivotOffset;
            pivot.position = target;

            Transform view = ViewTransform();
            if (view == null || view == pivot || view.IsChildOf(pivot))
                return;

            Vector3 expected = target + _eyeOffset;
            if (!_hasEyeOffset)
            {
                _eyeOffset = view.position - target;
                _hasEyeOffset = true;
                return;
            }

            if ((view.position - expected).sqrMagnitude > 0.25f * 0.25f)
                view.position = expected;
            else
                _eyeOffset = view.position - target;
        }

        void CaptureHeld()
        {
            _held.Clear();
            Transform camera = ViewTransform();
            if (camera == null || _player.ToolbarReferences == null)
                return;

            var toolbar = _player.ToolbarReferences;
            Transform root = toolbar.ToolbarTransform;
            bool movedRoot = TryAddHeld(root, camera);
            if (!movedRoot || !IsUnder(toolbar.SelectedItemPivot, root))
                TryAddHeld(toolbar.SelectedItemPivot, camera);
            if (!movedRoot || !IsUnder(toolbar.NonSelectedItemsPivot, root))
                TryAddHeld(toolbar.NonSelectedItemsPivot, camera);
        }

        bool TryAddHeld(Transform target, Transform camera)
        {
            if (target == null || target == camera || target.IsChildOf(camera))
                return false;
            if (OwnsPlayer(target))
                return false;

            _held.Add(new HeldAnchor
            {
                Target = target,
                LocalPosition = camera.InverseTransformPoint(target.position),
                LocalRotation = Quaternion.Inverse(camera.rotation) * target.rotation
            });
            return true;
        }

        void FollowHeld()
        {
            Transform camera = ViewTransform();
            if (camera == null)
                return;

            for (int i = 0; i < _held.Count; i++)
            {
                HeldAnchor anchor = _held[i];
                if (anchor.Target == null)
                    continue;
                anchor.Target.SetPositionAndRotation(
                    camera.TransformPoint(anchor.LocalPosition),
                    camera.rotation * anchor.LocalRotation);
            }
        }

        Transform ViewTransform()
        {
            if (_player.CameraService != null && _player.CameraService.CameraTransform != null)
                return _player.CameraService.CameraTransform;
            return Pivot();
        }

        bool OwnsPlayer(Transform target)
        {
            if (_body != null && (_body.transform == target || _body.transform.IsChildOf(target)))
                return true;
            Transform camera = ViewTransform();
            return camera != null && (camera == target || camera.IsChildOf(target));
        }

        static bool IsUnder(Transform child, Transform parent)
        {
            return child != null && parent != null && (child == parent || child.IsChildOf(parent));
        }

        Transform Pivot()
        {
            if (_player.CameraHandler != null && _player.CameraHandler.CameraPivot != null)
                return _player.CameraHandler.CameraPivot;
            if (_player.CameraService != null)
                return _player.CameraService.CameraTransform;
            return null;
        }

        /// <summary>When walking starts, puts the feet on top of whatever surface they are inside or just above.</summary>
        void LiftOntoFloor()
        {
            Vector3 position = _hasVisual ? _simPosition : _body.transform.position;
            Vector3 feet = Feet(position);
            Vector3 origin = feet + Vector3.up * 1.2f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 1.2f + SnapDistance, Physics.DefaultRaycastLayers)
                && IsGroundHit(hit.collider) && hit.normal.y >= MinWalkNormal)
            {
                float sink = hit.point.y + 0.02f - feet.y;
                if (sink > 0f)
                {
                    _simPosition = position + Vector3.up * sink;
                    _hasVisual = true;
                    ApplyPose();
                }
                return;
            }

            float below = _floorY - feet.y;
            if (below <= 0.02f)
                return;
            _simPosition = position + Vector3.up * (below + 0.05f);
            _hasVisual = true;
            ApplyPose();
        }

        void BuildGround()
        {
            EnsureCapsule();
            Ground.Build(_player != null ? _player.transform : null, _body.gameObject.layer, Movement.GenerateGround);
            _floorY = Ground.FloorY;
        }

        internal void RequestRebuild()
        {
            _built = false;
        }

        void ClearGround()
        {
            Ground.Clear();
        }
    }
}
