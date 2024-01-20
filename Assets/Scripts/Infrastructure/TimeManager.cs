using System;
using Unity.Netcode;
using UnityEngine;

public class TimeManager : NetworkBehaviour
{
    public static TimeManager Singleton { get; private set; }

    [SerializeField] private float _ticksPerSecond;
    public float DelayBetweenTicks { get; private set; }
    private float tickTimer;
    /// <summary>
    /// Raised on the client, comes before physics tick
    /// </summary>
    public event Action OnTick;


    [SerializeField] private float _physicsTicksPerSecond;
    public float DelayBetweenPhysicsTicks { get; private set; }
    private float physicsTickTimer;
    /// <summary>
    /// Raised on the client, comes after tick, and after <see cref="Physics2D.Simulate(float)"/>
    /// </summary>
    public event Action OnAfterPhysicsTick;
    /// <summary>
    /// Raised on the client, comes after tick, and before <see cref="Physics2D.Simulate(float)"/>
    /// Comes before the player motor physics tick
    /// </summary>
    public event Action OnBeforePhysicsTick;
    /// <summary>
    /// Raised on the client, comes after tick, and before <see cref="Physics2D.Simulate(float)"/>, but after <see cref="OnBeforePhysicsTick"/>.
    /// Shall only ever be used by the player motor (<see cref="PlayerMotor"/>)
    /// </summary>
    public event Action OnBeforePhysicsTickPlayer;

    private bool timerStarted;

    private void Awake()
    {
        Singleton = this;

        DelayBetweenTicks = 1f / _ticksPerSecond;
        DelayBetweenPhysicsTicks = 1f / _physicsTicksPerSecond;
    }

    public void StartTimerServerSided()
    {
        timerStarted = true;
        StartTimerClientRpc(DateTime.UtcNow);
    }

    [ClientRpc]
    private void StartTimerClientRpc(DateTime startTime)
    {
        if (IsServer)
            return; //Server already started timer

        var timeSinceStart = DateTime.UtcNow - startTime;
        tickTimer = (float)timeSinceStart.TotalSeconds % DelayBetweenTicks;
        timerStarted = true;
    }


    private void Update()
    {
        if (!timerStarted)
            return;
        
        tickTimer += Time.deltaTime;
        if (tickTimer >= DelayBetweenTicks)
        {
            tickTimer -= DelayBetweenTicks;
            OnTick?.Invoke();
        }

        physicsTickTimer += Time.deltaTime;
        if (physicsTickTimer >= DelayBetweenPhysicsTicks)
        {
            physicsTickTimer -= DelayBetweenPhysicsTicks;
            OnBeforePhysicsTick?.Invoke();
            OnBeforePhysicsTickPlayer?.Invoke();
            Physics2D.Simulate(DelayBetweenPhysicsTicks);
            OnAfterPhysicsTick?.Invoke();
        }
    }
}
