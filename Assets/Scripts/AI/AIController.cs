using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Basic AI state machine for CPU-controlled lacrosse players.
/// Requires the Unity AI Navigation package (com.unity.ai.navigation).
/// Updated for physics-only ball system — no CarrierRoot reference.
/// Unity 6000.x.
/// </summary>
[RequireComponent(typeof(NavMeshAgent), typeof(StickController))]
public class AIController : MonoBehaviour
{
    public enum AIState { Idle, SeekBall, CarryToGoal, Defend }

    [Header("Team")]
    public Team team;

    [Header("Targets")]
    public Transform      ownGoal;
    public Transform      enemyGoal;
    public BallController ball;

    [Header("Tuning")]
    public float seekSpeed    =  5f;
    public float attackSpeed  =  6f;
    public float defendSpeed  =  5.5f;
    public float shootRange   =  8f;
    public float stoppingDist =  0.5f;
    public float defendRadius = 12f;

    [Header("Decision")]
    public float decisionInterval = 0.4f;

    // Public state
    public AIState CurrentState { get; private set; } = AIState.Idle;

    private NavMeshAgent    _agent;
    private StickController _stick;
    private float           _decisionTimer;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _stick = GetComponent<StickController>();

        _agent.stoppingDistance = stoppingDist;
        _agent.angularSpeed     = 360f;
        _agent.acceleration     = 12f;
    }

    private void Start()
    {
        if (ball == null)
            ball = FindFirstObjectByType<BallController>();
    }

    private void Update()
    {
        if (GameManager.Instance == null ||
            GameManager.Instance.State != GameState.Playing)
        {
            _agent.isStopped = true;
            return;
        }

        _agent.isStopped = false;

        _decisionTimer -= Time.deltaTime;
        if (_decisionTimer <= 0f)
        {
            EvaluateState();
            _decisionTimer = decisionInterval;
        }

        ExecuteState();
    }

    // ── State evaluation ──────────────────────────────────────────────────────

    private void EvaluateState()
    {
        if (ball == null) { SetState(AIState.Idle); return; }

        if (_stick.HasBall)
        {
            SetState(AIState.CarryToGoal);
        }
        else if (!ball.IsInCup)
        {
            // Ball is loose — go get it
            SetState(AIState.SeekBall);
        }
        else
        {
            // Opponent has the ball — defend
            SetState(AIState.Defend);
        }
    }

    // ── State execution ───────────────────────────────────────────────────────

    private void ExecuteState()
    {
        switch (CurrentState)
        {
            case AIState.SeekBall:    ExecuteSeekBall();    break;
            case AIState.CarryToGoal: ExecuteCarryToGoal(); break;
            case AIState.Defend:      ExecuteDefend();      break;
            case AIState.Idle:                              break;
        }
    }

    private void ExecuteSeekBall()
    {
        if (ball == null) return;
        _agent.speed = seekSpeed;
        _agent.SetDestination(ball.transform.position);
    }

    private void ExecuteCarryToGoal()
    {
        if (enemyGoal == null) return;
        _agent.speed = attackSpeed;

        float dist = Vector3.Distance(transform.position, enemyGoal.position);
        if (dist <= shootRange)
        {
            _agent.isStopped = true;
            FaceTarget(enemyGoal.position);
            _stick.AIRelease(enemyGoal.position, 28f);
        }
        else
        {
            _agent.isStopped = false;
            _agent.SetDestination(enemyGoal.position);
        }
    }

    private void ExecuteDefend()
    {
        if (ball == null || ownGoal == null) return;
        _agent.speed = defendSpeed;

        Vector3 toBall    = (ball.transform.position - ownGoal.position).normalized;
        Vector3 defendPos = ownGoal.position + toBall * (defendRadius * 0.6f);
        _agent.SetDestination(defendPos);
        FaceTarget(ball.transform.position);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetState(AIState s) { if (CurrentState != s) CurrentState = s; }

    private void FaceTarget(Vector3 target)
    {
        Vector3 dir = (target - transform.position);
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        transform.rotation = Quaternion.LookRotation(dir);
    }

    private void OnDrawGizmosSelected()
    {
        if (enemyGoal != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(enemyGoal.position, shootRange);
        }
        if (ownGoal != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(ownGoal.position, defendRadius);
        }
    }
}
