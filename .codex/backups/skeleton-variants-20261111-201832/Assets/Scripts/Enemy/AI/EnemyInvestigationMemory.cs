using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class EnemyInvestigationMemory
{
    public readonly struct Snapshot
    {
        public Snapshot(PlayerHealth player, Vector3 position, float age, bool visited,
            EnemySearchArea signalArea, EnemySearchArea nextArea)
        {
            Player = player;
            Position = position;
            Age = age;
            Visited = visited;
            SignalArea = signalArea;
            NextArea = nextArea;
        }

        public PlayerHealth Player { get; }
        public Vector3 Position { get; }
        public float Age { get; }
        public bool Visited { get; }
        public EnemySearchArea SignalArea { get; }
        public EnemySearchArea NextArea { get; }
    }

    private sealed class Trace
    {
        public PlayerHealth Player;
        public Vector3 Position;
        public float LastSignalTime;
        public bool HasSignal;
        public bool PointVisited;
        public EnemySearchArea SignalArea;
        public EnemySearchArea OccupiedArea;
        public EnemySearchArea CandidateArea;
        public float CandidateSince;
        public EnemySearchArea NextArea;
    }

    private readonly Dictionary<PlayerHealth, Trace> traces = new Dictionary<PlayerHealth, Trace>();
    private readonly List<PlayerHealth> stalePlayers = new List<PlayerHealth>();
    private EnemySearchArea primaryArea;
    private PlayerHealth primaryPlayer;
    public EnemySearchArea PrimaryArea => primaryArea;
    public PlayerHealth PrimaryPlayer => primaryPlayer;
    public bool HasInvestigationOrigin => primaryPlayer != null || primaryArea != null;

    public void Record(PlayerHealth player, Vector3 position, float now, Scene scene)
    {
        if (player == null || !player.CanBeTargetedByEnemy)
            return;

        if (!traces.TryGetValue(player, out Trace trace))
        {
            trace = new Trace { Player = player };
            traces.Add(player, trace);
        }

        EnemySearchArea area = EnemySearchArea.FindContaining(position, scene);
        if (area == null)
            area = EnemySearchArea.FindContaining(player.transform.position, scene);

        if (!trace.HasSignal || (trace.Position - position).sqrMagnitude > 0.25f * 0.25f
            || area != null && area != trace.SignalArea)
            trace.PointVisited = false;

        if (area != null && area != trace.SignalArea)
        {
            trace.SignalArea = area;
            trace.NextArea = null;
        }

        trace.Position = position;
        trace.LastSignalTime = now;
        trace.HasSignal = true;
        if (trace.OccupiedArea == null)
            trace.OccupiedArea = area;
    }

    public void ObserveRoomChanges(float now, float confirmationTime, float memoryLifetime, Scene scene)
    {
        stalePlayers.Clear();
        foreach (KeyValuePair<PlayerHealth, Trace> pair in traces)
        {
            Trace trace = pair.Value;
            if (pair.Key == null || !pair.Key.CanBeTargetedByEnemy
                || now - trace.LastSignalTime > memoryLifetime)
            {
                stalePlayers.Add(pair.Key);
                continue;
            }

            Vector3 playerPosition = pair.Key.transform.position;
            EnemySearchArea area = trace.OccupiedArea != null && trace.OccupiedArea.isActiveAndEnabled
                && trace.OccupiedArea.ContainsPoint(playerPosition)
                    ? trace.OccupiedArea
                    : EnemySearchArea.FindContaining(playerPosition, scene);
            if (area == null || area == trace.OccupiedArea)
            {
                trace.CandidateArea = null;
                continue;
            }

            if (trace.OccupiedArea == null)
            {
                trace.OccupiedArea = area;
                continue;
            }

            if (trace.CandidateArea != area)
            {
                trace.CandidateArea = area;
                trace.CandidateSince = now;
                continue;
            }

            if (now - trace.CandidateSince < confirmationTime)
                continue;

            if (trace.SignalArea == trace.OccupiedArea && trace.NextArea == null)
                trace.NextArea = area;

            trace.OccupiedArea = area;
            trace.CandidateArea = null;
        }

        for (int i = 0; i < stalePlayers.Count; i++)
            traces.Remove(stalePlayers[i]);
    }

    public void BeginInvestigation(PlayerHealth player)
    {
        primaryPlayer = player;
        primaryArea = player != null && traces.TryGetValue(player, out Trace trace)
            ? trace.SignalArea : null;
    }

    public void MarkPointVisited(PlayerHealth player)
    {
        if (player != null && traces.TryGetValue(player, out Trace trace))
            trace.PointVisited = true;
    }

    public bool TryGetNextPoint(PlayerHealth excluding, float now, float lifetime,
        out PlayerHealth player, out Vector3 position)
    {
        player = null;
        position = Vector3.zero;
        float newest = float.NegativeInfinity;
        foreach (Trace trace in traces.Values)
        {
            if (trace.Player == null || !trace.Player.CanBeTargetedByEnemy
                || trace.Player == excluding || !trace.HasSignal || trace.PointVisited
                || now - trace.LastSignalTime > lifetime || trace.LastSignalTime <= newest)
                continue;

            newest = trace.LastSignalTime;
            player = trace.Player;
            position = trace.Position;
        }

        return player != null;
    }

    public bool TryGetSignalArea(PlayerHealth player, out EnemySearchArea area)
    {
        area = player != null && traces.TryGetValue(player, out Trace trace)
            ? trace.SignalArea : null;
        return area != null;
    }

    public void BuildSearchRoute(List<EnemySearchArea> route, float now, float lifetime)
    {
        route.Clear();
        AddArea(route, primaryArea);
        if (primaryPlayer != null && traces.TryGetValue(primaryPlayer, out Trace primaryTrace)
            && now - primaryTrace.LastSignalTime <= lifetime)
            AddArea(route, primaryTrace.NextArea);

        var ordered = new List<Trace>();
        foreach (Trace trace in traces.Values)
        {
            if (trace.HasSignal && now - trace.LastSignalTime <= lifetime)
                ordered.Add(trace);
        }
        ordered.Sort((a, b) => b.LastSignalTime.CompareTo(a.LastSignalTime));
        foreach (Trace trace in ordered)
            AddArea(route, trace.SignalArea);

        foreach (Trace trace in ordered)
            AddArea(route, trace.NextArea);
    }

    public void Clear()
    {
        traces.Clear();
        ClearOrigin();
    }

    public void ClearOrigin()
    {
        primaryArea = null;
        primaryPlayer = null;
    }

    public void Capture(List<Snapshot> snapshots, float now, float lifetime)
    {
        snapshots.Clear();
        foreach (Trace trace in traces.Values)
        {
            if (trace.Player == null || !trace.Player.CanBeTargetedByEnemy
                || !trace.HasSignal || now - trace.LastSignalTime > lifetime)
                continue;

            snapshots.Add(new Snapshot(trace.Player, trace.Position,
                Mathf.Max(0f, now - trace.LastSignalTime), trace.PointVisited,
                trace.SignalArea, trace.NextArea));
        }
    }

    public void Restore(List<Snapshot> snapshots, EnemySearchArea restoredPrimaryArea,
        PlayerHealth restoredPrimaryPlayer, float now, float transportAge)
    {
        Clear();
        primaryArea = restoredPrimaryArea;
        primaryPlayer = restoredPrimaryPlayer;
        foreach (Snapshot snapshot in snapshots)
        {
            if (snapshot.Player == null || !snapshot.Player.CanBeTargetedByEnemy)
                continue;

            traces[snapshot.Player] = new Trace
            {
                Player = snapshot.Player,
                Position = snapshot.Position,
                LastSignalTime = now - Mathf.Max(0f, snapshot.Age + transportAge),
                HasSignal = true,
                PointVisited = snapshot.Visited,
                SignalArea = snapshot.SignalArea,
                OccupiedArea = snapshot.SignalArea,
                NextArea = snapshot.NextArea
            };
        }
    }

    private static void AddArea(List<EnemySearchArea> route, EnemySearchArea area)
    {
        if (area != null && area.isActiveAndEnabled && !route.Contains(area))
            route.Add(area);
    }
}
