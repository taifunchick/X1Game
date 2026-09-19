//Copyright 2022, Infima Games. All Rights Reserved.

using System;
using System.Collections;
using UnityEngine;

namespace InfimaGames.LowPolyShooterPack
{
    /// <summary>
    /// Movement. This is our main, and base, component that handles the character's movement.
    /// It contains all of the logic relating to moving, running, crouching, jumping...etc
    /// </summary>
    public class Movement : MovementBehaviour
    {
        #region FIELDS SERIALIZED
        
        
        [Tooltip("How fast the character's speed increases.")]
        [SerializeField]
        private float acceleration = 9.0f;

        [Tooltip("Acceleration value used when the character is in the air. This means either jumping, or falling.")]
        [SerializeField]
        private float accelerationInAir = 3.0f;

        [Tooltip("How fast the character's speed decreases.")]
        [SerializeField]
        private float deceleration = 11.0f;
        

        [Tooltip("The speed of the player while walking.")]
        [SerializeField]
        private float speedWalking = 4.0f;
        
        [Tooltip("How fast the player moves while aiming.")]
        [SerializeField]
        private float speedAiming = 3.2f;
        
        [Tooltip("How fast the player moves while aiming.")]
        [SerializeField]
        private float speedCrouching = 3.5f;

        [Tooltip("How fast the player moves while running."), SerializeField]
        private float speedRunning = 6.8f;
        
        
        [Tooltip("Value to multiply the walking speed by when the character is moving forward."), SerializeField]
        [Range(0.0f, 1.0f)]
        private float walkingMultiplierForward = 1.0f;

        [Tooltip("Value to multiply the walking speed by when the character is moving sideways.")]
        [Range(0.0f, 1.0f)]
        [SerializeField]
        private float walkingMultiplierSideways = 1.0f;

        [Tooltip("Value to multiply the walking speed by when the character is moving backwards.")]
        [Range(0.0f, 1.0f)]
        [SerializeField]
        private float walkingMultiplierBackwards = 1.0f;
        

        [Tooltip("How much control the player has over changes in direction while the character is in the air.")]
        [Range(0.0f, 1.0f)]
        [SerializeField]
        private float airControl = 0.8f;

        [Tooltip("The value of the character's gravity. Basically, defines how fast the character falls.")]
        [SerializeField]
        private float gravity = 1.1f;

        [Tooltip("The value of the character's gravity while jumping.")]
        [SerializeField]
        private float jumpGravity = 1.0f;

        [Tooltip("The force of the jump.")]
        [SerializeField]
        private float jumpForce = 100.0f;

        [Tooltip("Force applied to keep the character from flying away while descending slopes.")]
        [SerializeField]
        private float stickToGroundForce = 0.03f;


        [Tooltip("Setting this to false will always block the character from crouching.")]
        [SerializeField]
        private bool canCrouch = true;

        [Tooltip("If true, the character will be able to crouch/un-crouch while falling, which can lead to " +
                 "some slightly interesting results.")]
        [SerializeField, ShowIf(nameof(canCrouch), true)]
        private bool canCrouchWhileFalling = false;

        [Tooltip("If true, the character will be able to jump while crouched too!")]
        [SerializeField, ShowIf(nameof(canCrouch), true)]
        private bool canJumpWhileCrouching = true;

        [Tooltip("Height of the character while crouching.")]
        [SerializeField, ShowIf(nameof(canCrouch), true)]
        private float crouchHeight = 1.0f;
        
        [Tooltip("Mask of possible layers that can cause overlaps when trying to un-crouch. Very important!")]
        [SerializeField, ShowIf(nameof(canCrouch), true)]
        private LayerMask crouchOverlapsMask;


        [Tooltip("Force applied to other rigidbodies when walking into them. This force is multiplied by the character's " +
                 "velocity, so it is never applied by itself, that's important to note.")]
        [SerializeField]
        private float rigidbodyPushForce = 1.0f;

        #endregion

        #region FIELDS

        /// <summary>
        /// Controller.
        /// </summary>
        private CharacterController controller;

        /// <summary>
        /// Player Character.
        /// </summary>
        private CharacterBehaviour playerCharacter;
        /// <summary>
        /// The player character's equipped weapon.
        /// </summary>
        private WeaponBehaviour equippedWeapon;

        /// <summary>
        /// Default height of the character.
        /// </summary>
        private float standingHeight;

        /// <summary>
        /// Velocity.
        /// </summary>
        private Vector3 velocity;

        /// <summary>
        /// Is the character on the ground.
        /// </summary>
        private bool isGrounded;
        /// <summary>
        /// Was the character standing on the ground last frame.
        /// </summary>
        private bool wasGrounded;

        /// <summary>
        /// Is the character jumping?
        /// </summary>
        private bool jumping;
        /// <summary>
        /// If true, the character controller is crouched.
        /// </summary>
        private bool crouching;

        /// <summary>
        /// Stores the Time.time value when the character last jumped.
        /// </summary>
        private float lastJumpTime;

        /// <summary>
        /// True once we've complained about a missing CharacterController. We only do this once,
        /// because these functions can be called every frame, by many different components.
        /// </summary>
        private bool loggedMissingController;
        
        #endregion

        #region UNITY FUNCTIONS

        /// <summary>
        /// Awake.
        /// </summary>
        protected override void Awake()
        {
            /*
             * Cache the controller as early as possible! Other components (the Motion components, the
             * FootstepPlayer, the Crosshair...) can query this Movement before our Start() has run, which
             * happens when this component is disabled at spawn time, as it is for remote players in a
             * networked game. Reading a null CharacterController there throws a NullReferenceException
             * every single frame.
             */
            CacheController();

            //Cache the character we should be reading input and weapon information from.
            CachePlayerCharacter();
        }
        /// Initializes the FpsController on start.
        protected override void Start()
        {
            //Cache the controller.
            CacheController();

            //Save the default height.
            if (controller != null)
                standingHeight = controller.height;
        }

        /// Moves the camera to the character, processes jumping and plays sounds every frame.
        protected override void Update()
        {
            //Get the equipped weapon!
            equippedWeapon = GetEquippedWeapon();
            
            //Get this frame's grounded value.
            isGrounded = IsGrounded();
            //Check if it has changed from last frame.
            if (isGrounded && !wasGrounded)
            {
                //Set jumping.
                jumping = false;
                //Set lastJumpTime.
                lastJumpTime = 0.0f;
            }
            else if (wasGrounded && !isGrounded)
                lastJumpTime = Time.time;
            
            //Move.
            MoveCharacter();
            //Save the grounded value to check for difference next frame.
            wasGrounded = isGrounded;
        }
        /// <summary>
        /// OnControllerColliderHit.
        /// </summary>
        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            //Zero out the upward velocity if the character hits the ceiling.
            if (hit.moveDirection.y > 0.0f && velocity.y > 0.0f)
                velocity.y = 0.0f;

            //We need a rigidbody to push the object we just hit.
            Rigidbody hitRigidbody = hit.rigidbody;
            if (hitRigidbody == null)
                return;
            
            //AddForce.
            Vector3 force = (hit.moveDirection + Vector3.up * 0.35f) * velocity.magnitude * rigidbodyPushForce;
            hitRigidbody.AddForceAtPosition(force, hit.point);
        }
        
        #endregion

        #region METHODS

        /// <summary>
        /// Caches this character's CharacterController, if it hasn't been cached already.
        /// </summary>
        private void CacheController()
        {
            if (controller == null)
                controller = GetComponent<CharacterController>();
        }
        /// <summary>
        /// Caches the CharacterBehaviour this movement should read input and weapon information from.
        /// </summary>
        private void CachePlayerCharacter()
        {
            //Prefer the character on this player, so remote players never drive local movement.
            playerCharacter = GetComponent<CharacterBehaviour>();
            if (playerCharacter != null)
                return;

            //Fall back to the character registered with the game mode service. This can legitimately be
            //unavailable (a dedicated server, or a scene without a Bootstraper), so we never let it throw:
            //an exception in Awake would leave the whole player prefab half-initialized.
            try
            {
                IGameModeService gameModeService = ServiceLocator.Current?.Get<IGameModeService>();
                if (gameModeService != null)
                    playerCharacter = gameModeService.GetPlayerCharacter();
            }
            catch (InvalidOperationException)
            {
                //The service isn't registered. We simply have no character to read from.
            }
        }
        /// <summary>
        /// Returns the character's currently equipped weapon, or null if there is none available.
        /// </summary>
        private WeaponBehaviour GetEquippedWeapon()
        {
            //Re-cache if we somehow lost the character (scene changes, destroyed references...).
            if (playerCharacter == null)
                CachePlayerCharacter();

            //Without a character we cannot know which weapon is equipped.
            if (playerCharacter == null)
                return null;

            //The inventory may not be ready yet either.
            InventoryBehaviour inventory = playerCharacter.GetInventory();
            return inventory != null ? inventory.GetEquipped() : null;
        }
        /// <summary>
        /// Makes sure that we have a usable CharacterController, complaining once if we don't.
        /// </summary>
        private bool HasController()
        {
            //Try to cache it, in case it was added to this GameObject after our Awake.
            CacheController();

            if (controller != null)
                return true;

            //Complain once. This function can be called every frame by many components, and spamming
            //the console would only hide the actual problem.
            if (!loggedMissingController)
            {
                loggedMissingController = true;
                Log.ReferenceError(this, gameObject);
            }

            return false;
        }

        /// <summary>
        /// Moves the character.
        /// </summary>
        private void MoveCharacter()
        {
            //Without a controller there is nothing to move, and reading it would throw.
            if (!HasController())
                return;

            //Get Movement Input! We have none without a character to read it from.
            Vector2 frameInput = playerCharacter != null
                ? Vector3.ClampMagnitude(playerCharacter.GetInputMovement(), 1.0f)
                : Vector2.zero;
            //Calculate local-space direction by using the player's input.
            var desiredDirection = new Vector3(frameInput.x, 0.0f, frameInput.y);
            
            //Running speed calculation.
            if (playerCharacter != null && playerCharacter.IsRunning())
                desiredDirection *= speedRunning;
            else
            {
                //Crouching Speed.
                if (crouching)
                    desiredDirection *= speedCrouching;
                else
                {
                    //Aiming speed calculation.
                    if (playerCharacter != null && playerCharacter.IsAiming())
                        desiredDirection *= speedAiming;
                    else
                    {
                        //Multiply by the normal walking speed.
                        desiredDirection *= speedWalking;
                        //Multiply by the sideways multiplier, to get better feeling sideways movement.
                        desiredDirection.x *= walkingMultiplierSideways;
                        //Multiply by the forwards and backwards multiplier.
                        desiredDirection.z *=
                            (frameInput.y > 0 ? walkingMultiplierForward : walkingMultiplierBackwards);
                    }
                }
            } 

            //World space velocity calculation.
            desiredDirection = transform.TransformDirection(desiredDirection);
            //Multiply by the weapon movement speed multiplier. This helps us modify speeds based on the weapon!
            if (equippedWeapon != null)
                desiredDirection *= equippedWeapon.GetMultiplierMovementSpeed();
            
            //Apply gravity!
            if (isGrounded == false)
            {
                //Get rid of any upward velocity.
                if (wasGrounded && !jumping)
                    velocity.y = 0.0f;
                
                //Movement.
                velocity += desiredDirection * (accelerationInAir * airControl * Time.deltaTime);
                //Gravity.
                velocity.y -= (velocity.y >= 0 ? jumpGravity : gravity) * Time.deltaTime;
            }
            //Normal Movement On Ground.
            else if(!jumping)
            {
                //Update velocity with movement on the ground values.
                velocity = Vector3.Lerp(velocity, new Vector3(desiredDirection.x, velocity.y, desiredDirection.z), Time.deltaTime * (desiredDirection.sqrMagnitude > 0.0f ? acceleration : deceleration));
            }

            //Velocity Applied.
            Vector3 applied = velocity * Time.deltaTime;
            //Stick To Ground Force. Helps with making the character walk down slopes without floating.
            if (controller.isGrounded && !jumping)
                applied.y = -stickToGroundForce;

            //Move.
            controller.Move(applied);
        }

        /// <summary>
        /// WasGrounded.
        /// </summary>
        public override bool WasGrounded() => wasGrounded;
        /// <summary>
        /// IsJumping.
        /// </summary>
        public override bool IsJumping() => jumping;

        /// <summary>
        /// Can Crouch.
        /// </summary>
        public override bool CanCrouch(bool newCrouching)
        {
            //Always block crouching if we need to.
            if (canCrouch == false)
                return false;

            //If we're in the air, and we cannot crouch while in the air, then we can ignore this execution!
            if (isGrounded == false && canCrouchWhileFalling == false)
                return false;
            
            //The controller can always crouch, the issue is un-crouching!
            if (newCrouching)
                return true;

            //We cannot check for overlaps without a controller. Staying crouched is the safe answer.
            if (!HasController())
                return false;

            //Overlap check location.
            Vector3 sphereLocation = transform.position + Vector3.up * standingHeight;
            //Check for any overlaps.
            return (Physics.OverlapSphere(sphereLocation, controller.radius, crouchOverlapsMask).Length == 0);
        }

        /// <summary>
        /// IsCrouching.
        /// </summary>
        /// <returns></returns>
        public override bool IsCrouching() => crouching;

        /// <summary>
        /// Jump.
        /// </summary>
        public override void Jump()
        {
            //We can ignore this if we're crouching and we're not allowed to do crouch-jumps.
            if (crouching && !canJumpWhileCrouching)
                return;
            
            //Block jumping when we're not grounded. This avoids us double jumping.
            if (!isGrounded)
                return;

            //Jump.
            jumping = true;
            //Apply Jump Velocity.
            velocity = new Vector3(velocity.x, Mathf.Sqrt(2.0f * jumpForce * jumpGravity), velocity.z);

            //Save lastJumpTime.
            lastJumpTime = Time.time;
        }
        /// <summary>
        /// Changes the controller's capsule height.
        /// </summary>
        public override void Crouch(bool newCrouching)
        {
            //Set the new crouching value.
            crouching = newCrouching;

            //There is no capsule to resize without a controller.
            if (!HasController())
                return;

            //Update the capsule's height.
            controller.height = crouching ? crouchHeight : standingHeight;
            //Update the capsule's center.
            controller.center = controller.height / 2.0f * Vector3.up;
        }

        public override void TryCrouch(bool value)
        {
            //Crouch.
            if (value && CanCrouch(true))
                Crouch(true);
            //Coroutine Un-Crouch.
            else if(!value)
                StartCoroutine(nameof(TryUncrouch));
        }

        /// <summary>
        /// Try Toggle Crouch.
        /// </summary>
        public override void TryToggleCrouch() => TryCrouch(!crouching);
        /// <summary>
        /// Tries to un-crouch the character.
        /// </summary>
        private IEnumerator TryUncrouch()
        {
            //If the movementBehaviour says that we can't go into whatever crouching state is the opposite, then
            //the character will have to forget about it, no way around it bois!
            yield return new WaitUntil(() => CanCrouch(false));
            
            //Un-Crouch.
            Crouch(false);
        }

        #endregion

        #region GETTERS

        /// <summary>
        /// GetLastJumpTime.
        /// </summary>
        public override float GetLastJumpTime() => lastJumpTime;

        /// <summary>
        /// Get Multiplier Forward.
        /// </summary>
        public override float GetMultiplierForward() => walkingMultiplierForward;
        /// <summary>
        /// Get Multiplier Sideways.
        /// </summary>
        public override float GetMultiplierSideways() => walkingMultiplierSideways;
        /// <summary>
        /// Get Multiplier Backwards.
        /// </summary>
        public override float GetMultiplierBackwards() => walkingMultiplierBackwards;
        
        /// <summary>
        /// Returns the value of Velocity.
        /// </summary>
        public override Vector3 GetVelocity() => controller != null ? controller.velocity : velocity;
        /// <summary>
        /// Returns the value of Grounded.
        /// </summary>
        /// <remarks>
        /// A character without a controller is treated as grounded. This keeps components that read this
        /// value every frame (the Jump/Land Motions, the FootstepPlayer, the Crosshair...) from believing
        /// that the character is permanently falling, and from throwing a NullReferenceException.
        /// </remarks>
        public override bool IsGrounded() => controller == null || controller.isGrounded;
        /// <summary>
        /// Returns true while this component is actively simulating a character, meaning that it is enabled,
        /// and that its dependencies are ready. Motion components use this to skip characters that are not
        /// being simulated locally, such as remote players in a networked game.
        /// </summary>
        public override bool IsSimulated() => isActiveAndEnabled && controller != null;

        #endregion
    }
}