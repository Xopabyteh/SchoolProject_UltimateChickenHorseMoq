using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMotor : NetworkBehaviour
{
    [Header("Infra")]
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private Animator animator;
    private static readonly int AnimRunning = Animator.StringToHash("Running");
    private static readonly int AnimJump = Animator.StringToHash("Jump");
    private static readonly int AnimLand = Animator.StringToHash("Land");
    [SerializeField] private Transform visualsTransform;
    [SerializeField] private InputActionReference movementAxisInput;
    [SerializeField] private InputActionReference jumpInput;
    [SerializeField] private InputActionReference sprintInput;
    private bool direction;
    private bool isImmobile;
    private bool isSetForWallSliding; //The phys mat

    [Header("Stats")]
    [SerializeField] private float horizontalSpeedStat = 4;
    [SerializeField] private float sprintBoostStat = 2;
    [SerializeField] private float jumpForceStat = 6f;
    [SerializeField] [Range(0, 1)] private float jumpCancelForceMult = 0.3f;
    private bool isJumping;
    [SerializeField] private float koyoteJumpTimeStat = 0.2f;
    [SerializeField] private Vector2 wallJumpForceStat = new(4f, 6f);
    private bool isWallJumping;
    private bool canResetIsWallJumping;
    [SerializeField] [Range(0, 1)] private float inAirHorizontalControlStat = 0.5f;
    [SerializeField] private PhysicsMaterial2D matForMoving;
    [SerializeField] private PhysicsMaterial2D matForStaying;
    [SerializeField] private PhysicsMaterial2D matForWallSliding;

    [Header("Checks")]
    [SerializeField] private LayerMask groundLayerMask;
    [SerializeField] private Transform groundCheckOriginTransform;
    [SerializeField] private Vector2 groundCheckSize;
    private float lastGroundedTime;

    [SerializeField] private Transform wallCheckOriginTransform;
    [SerializeField] private Vector2 wallCheckSize;
    [SerializeField] private float wallCheckDistance;

    /// <summary>
    /// wallDirection = -1 means wall is to the left of the player
    /// wallDirection = 1 means wall is to the right of the player
    /// wallDirection = 0 means wall on both sides
    /// </summary>
    private (float Time, float WallDirection) lastWallHug;
    public bool PlayerHasAuthority { get; private set; } = false;

    //These are added for this tick and are set to 0 at the end of the tick
    private Vector2 externalForceThisPhysicsTick = Vector2.zero;

    /// <summary>
    /// Adds a force to the player for this tick
    /// The force is set to 0 at the end of the tick
    ///
    /// The force is added in the <see cref="OnBeforePhysicsPlayer"/> or <see cref="TimeManager.OnBeforePhysicsTickPlayer"/>.
    /// The force must thus be added before that event.
    /// </summary>
    /// <param name="forceForThisTick"></param>
    public void AddExternalForceForThisTick(Vector2 forceForThisTick)
    {
        externalForceThisPhysicsTick += forceForThisTick;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            TimeManager.Singleton.OnTick += OnTickServer;
        }

        if (!IsOwner)
        {
            rb.isKinematic = true;
            return;
        }

        movementAxisInput.action.Enable();
        jumpInput.action.Enable();
        sprintInput.action.Enable();

        jumpInput.action.performed += OnJumpInput;
        jumpInput.action.canceled += OnJumpCanceled;
        TimeManager.Singleton.OnBeforePhysicsTickPlayer += OnBeforePhysicsPlayer;
        TimeManager.Singleton.OnAfterPhysicsTick += OnAfterPhysics;
        TimeManager.Singleton.OnTick += OnTick;
    }


    /// <summary>
    /// Makes the player unable to control his character
    /// and simulates the body on the server
    /// </summary>
    public void RemovePlayerAuthorityServerSided()
    {
        PlayerHasAuthority = false;
        rb.isKinematic = false; //So the server can simulate the body

        //Tell the player that he can't control his character
        RemovePlayerAuthorityClientRpc();
    }

    [ClientRpc]
    private void RemovePlayerAuthorityClientRpc()
    {
        PlayerHasAuthority = false;
        if (!IsServer) //Host is simulating the body so he doesn't need to set it to kinematic to watch it
        {
            rb.isKinematic = true;
        }
    }

    /// <summary>
    /// Makes the player able to control his character
    /// and he is simulated on the client
    /// </summary>
    public void EnablePlayerAuthorityServerSided()
    {
        PlayerHasAuthority = true;
        rb.isKinematic = true; //So the server can watch the client's simulation

        //Tell the player that he can control his character
        EnablePlayerAuthorityClientRpc();
    }

    [ClientRpc]
    private void EnablePlayerAuthorityClientRpc()
    {
        PlayerHasAuthority = true;
        if (IsOwner)
        {
            rb.isKinematic = false; //So the owner can control the body again
        }
    }

    /// <summary>
    /// Gives the player a lot of friction
    /// </summary>
    public void SetForImmobile()
    {
        isImmobile = true;
        isSetForWallSliding = false;
        rb.sharedMaterial = matForStaying;
        rb.velocity = Vector2.zero;
    }

    /// <summary>
    /// Puts the player back to normal
    /// </summary>
    public void SetForMovement()
    {
        isImmobile = false;
        isSetForWallSliding = false;
        rb.sharedMaterial = matForMoving;
    }

    private void SetForWallSlide()
    {
        rb.sharedMaterial = matForWallSliding;
        isSetForWallSliding = true;
    }

    private void OnJumpCanceled(InputAction.CallbackContext obj)
    {
        if (!PlayerHasAuthority) 
            return;

        if (!isJumping)
            return;

        isJumping = false;
        rb.velocityY *= jumpCancelForceMult;
    }

    private void OnJumpInput(InputAction.CallbackContext obj)
    {
        if (!PlayerHasAuthority)
            return;

        //Do wall jump if can
        //If not, do normal jump
        //If not, do nothing

        //Wall jump
        if (lastWallHug.Time + koyoteJumpTimeStat > Time.time)
        {
            //wallJumpHorizontalForce = lastWallHug.WallDirection * wallJumpForceStat.x;
            //rb.velocityY = wallJumpForceStat.y;
            //externalForces.Add(new FixedLengthLifeDecayingForce(wallJumpForceStat, Time.time, 0.8f));
            rb.velocity = new Vector2(-lastWallHug.WallDirection * wallJumpForceStat.x, wallJumpForceStat.y);

            isWallJumping = true;
            canResetIsWallJumping = false;
            CancelInvoke(nameof(SetCanResetIsWallJumping));
            Invoke(nameof(SetCanResetIsWallJumping), 0.1f);
            return;
        }

        //Normal jump
        if (CanJump())
        {
            isJumping = true;
            rb.velocity = new Vector2(rb.velocity.x, jumpForceStat);
            animator.SetTrigger(AnimJump);
        }
    }

    private void SetCanResetIsWallJumping()
    {
        canResetIsWallJumping = true;
    }

    private void OnBeforePhysicsPlayer()
    {
        if (!PlayerHasAuthority)
            return;

        var isGrounded = SetLastGroundedTime();
        SetLastWallHug();

        var movementAxis = movementAxisInput.action.ReadValue<Vector2>();
        var isHoldingSprint = sprintInput.action.ReadValue<float>(); // 1 or 0
        if (movementAxis.x != 0f && canResetIsWallJumping)
        {
            isWallJumping = false;
        }

        if (isWallJumping)
        {
            //Flip away from wall
            if (lastWallHug.WallDirection != 0)
            {
                direction = lastWallHug.WallDirection > 0;
                visualsTransform.localScale = new Vector3(-lastWallHug.WallDirection, 1, 1);
            }

            //Only add external force
            rb.velocity += externalForceThisPhysicsTick;

            return;
        }

        //Flip visuals
        if (movementAxis.x != 0)
        {
            direction = movementAxis.x > 0;
            visualsTransform.localScale = new Vector3(direction ? 1 : -1, 1, 1);
        }

        //Move
        if (isGrounded)
        {
            var horizontalMovement = (horizontalSpeedStat + isHoldingSprint * sprintBoostStat) * movementAxis.x;
            rb.velocity = new Vector2(horizontalMovement, rb.velocity.y);
            animator.SetBool(AnimRunning, movementAxis.x != 0);
        }
        else //In air
        {
            var velocity = rb.velocity; //Cache
            var horizontalMovement = horizontalSpeedStat * movementAxis.x * inAirHorizontalControlStat;

            if (horizontalMovement > 0 && velocity.x < horizontalSpeedStat)
                velocity.x += horizontalMovement;
            else if (horizontalMovement < 0 && velocity.x > -horizontalSpeedStat)
                velocity.x += horizontalMovement;

            rb.velocity = velocity;
            animator.SetBool(AnimRunning, false); //Can't run on air
        }
        
        rb.velocity += externalForceThisPhysicsTick;
    }

    private void OnAfterPhysics()
    {
        externalForceThisPhysicsTick = Vector2.zero;
    }

    private void OnTick()
    {
        if (!PlayerHasAuthority)
            return;

        var state = new StatePayload(rb.position, rb.velocity, direction);

        SetStateServerRpc(state);
    }

    private void OnTickServer()
    {
        if (PlayerHasAuthority)
            return;

        UpdateStateFromServerToClients();
    }
    


    #region State sync

    /// <summary>
    /// Call this from the client to propagate his state to the server and to other clients.
    /// </summary>
    /// <param name="state"></param>
    [ServerRpc(RequireOwnership = true)]
    private void SetStateServerRpc(StatePayload state)
    {
        if (!PlayerHasAuthority)
            return;

        if (!IsHost)
        {
            SetState(state);
        }

        SetStateClientRpc(state, true);
    }

    /// <summary>
    /// Set the state on all clients
    /// </summary>
    /// <param name="state"></param>
    /// <param name="checkOwnership">Use true if the owner already predicted his state, false if not. If it's true, the owner of this won't set the state</param>
    [ClientRpc]
    private void SetStateClientRpc(StatePayload state, bool checkOwnership)
    {
        if (checkOwnership && IsOwner)
            return;

        SetState(state);
    }

    private void SetState(StatePayload state)
    {
        rb.position = state.Position;
        rb.velocity = state.Velocity;
        
        direction = state.Direction;
        visualsTransform.localScale = new Vector3(direction ? 1 : -1, 1, 1);
    }

    /// <summary>
    /// Sets the state and sets it on all clients, not caring about ownership.
    /// </summary>
    /// <param name="state"></param>
    public void SetStateServerSided(StatePayload state)
    {
        SetState(state);
        SetStateClientRpc(state, false);
    }

    /// <summary>
    /// Take the current state on the server and set it on clients (don't set it twice on the server, only on the other clients)
    /// </summary>
    private void UpdateStateFromServerToClients()
    {
        if (PlayerHasAuthority)
            return;

        var state = new StatePayload(rb.position, rb.velocity, direction);
        UpdateStateFromServerToClientRpc(state);
    }

    [ClientRpc]
    private void UpdateStateFromServerToClientRpc(StatePayload state)
    {
        if (IsServer)
            return; //Server already has the state

        SetState(state);
    }


    public struct StatePayload : INetworkSerializable
    {
        public Vector2 Position;
        public Vector2 Velocity;

        /// <summary>
        /// True: 1 -> Right
        /// False: -1 -> Left
        /// </summary>
        public bool Direction;
        public StatePayload(Vector2 position, Vector2 velocity, bool direction)
        {
            Position = position;
            Velocity = velocity;
            Direction = direction;
        }
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Velocity);
            serializer.SerializeValue(ref Direction);
        }
    }

    #endregion

    /// <returns>True if the player is on the ground</returns>
    private bool SetLastGroundedTime()
    {
        var isGrounded =
            Physics2D.OverlapBox(groundCheckOriginTransform.position, groundCheckSize, 0f, groundLayerMask);

        if (!isGrounded)
            return false;

        const float resetJumpingVarsThreshold = 0.1f;
        if (lastGroundedTime + resetJumpingVarsThreshold < Time.time)
        {
            animator.SetTrigger(AnimLand);
            isJumping = false;
            isWallJumping = false;
        }

        lastGroundedTime = Time.time;
        return isGrounded;
    }

    private bool CanJump()
        => lastGroundedTime + koyoteJumpTimeStat > Time.time;

    private void SetLastWallHug()
    {
        var wallCheckOriginTransformPosition = wallCheckOriginTransform.position; // Cache

        var wallToLeftHit = Physics2D.OverlapBox(wallCheckOriginTransformPosition + Vector3.left * wallCheckDistance, wallCheckSize, 0f, groundLayerMask);
        var wallToRightHit = Physics2D.OverlapBox(wallCheckOriginTransformPosition + Vector3.right * wallCheckDistance, wallCheckSize, 0f, groundLayerMask);

        var wallToLeft = wallToLeftHit != null;
        var wallToRight = wallToRightHit != null;

        if (!wallToLeft && !wallToRight)
            return;

        var wallDirection = 0f;
        if (wallToLeft)
            wallDirection -= 1f;
        if (wallToRight)
            wallDirection += 1f;

        const float resetJumpingVarsThreshold = 0.1f;
        if (lastWallHug.Time + resetJumpingVarsThreshold < Time.time)
        {
            isJumping = false;
            isWallJumping = false;

            //Reset from wall slide to movement rb phys mat
            if (!isImmobile)
            {
                SetForMovement();
            }
        }

        lastWallHug = (Time: Time.time, WallDirection: wallDirection);
        if (!isSetForWallSliding && !isImmobile)
        {
            //Set to wall slide rb phys mat
            SetForWallSlide();
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;

        //Ground check
        Gizmos.DrawWireCube(groundCheckOriginTransform.position, groundCheckSize);

        //Wall checks
        var wallCheckOriginTransformPosition = wallCheckOriginTransform.position; // Cache
        Gizmos.DrawWireCube(wallCheckOriginTransformPosition + Vector3.left * wallCheckDistance, wallCheckSize);
        Gizmos.DrawWireCube(wallCheckOriginTransformPosition + Vector3.right * wallCheckDistance, wallCheckSize);

        if (!PlayerHasAuthority)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(transform.position + Vector3.up, 0.3f);
        }
    }
}