using System.Collections.Generic;
using UnityEngine;

public class AStarPathfinder3D : MonoBehaviour
{
    public static AStarPathfinder3D Instance { get; private set; }

    [Header("Grid Settings")]
    public float gridWidth = 60f;
    public float gridDepth = 40f;
    public float cellSize = 1f;

    [Header("Obstacle Settings")]
    public LayerMask obstacleMask;
    public float obstacleCheckRadius = 0.45f;
    public float minimumAgentClearance = 0.5f;

    [Header("Debug")]
    public bool drawGrid = true;
    public float gizmoHeight = 0.05f;

    private PathNode[,] grid;
    private int gridSizeX;
    private int gridSizeZ;

    private void Awake()
    {
        Instance = this;
        CreateGrid();
    }

    private void CreateGrid()
    {
        gridSizeX = Mathf.RoundToInt(gridWidth / cellSize);
        gridSizeZ = Mathf.RoundToInt(gridDepth / cellSize);

        grid = new PathNode[gridSizeX, gridSizeZ];

        Vector3 bottomLeft = transform.position - new Vector3(gridWidth / 2f, 0f, gridDepth / 2f);

        for (int x = 0; x < gridSizeX; x++)
        {
            for (int z = 0; z < gridSizeZ; z++)
            {
                Vector3 worldPosition = bottomLeft + new Vector3(
                    x * cellSize + cellSize / 2f,
                    0f,
                    z * cellSize + cellSize / 2f
                );

                bool blocked = Physics.CheckSphere(
                    worldPosition + Vector3.up * 0.5f,
                    Mathf.Max(obstacleCheckRadius, minimumAgentClearance),
                    obstacleMask
                );

                bool walkable = !blocked;

                grid[x, z] = new PathNode(walkable, worldPosition, x, z);
            }
        }
    }

    public List<Vector3> FindPath(Vector3 startWorldPosition, Vector3 targetWorldPosition)
    {
        if (grid == null)
        {
            CreateGrid();
        }

        ResetNodes();

        PathNode startNode = NodeFromWorldPoint(startWorldPosition);
        PathNode targetNode = NodeFromWorldPoint(targetWorldPosition);

        if (startNode == null || targetNode == null)
        {
            return null;
        }

        if (!targetNode.walkable)
        {
            targetNode = FindNearestWalkableNode(targetNode);
        }

        if (!startNode.walkable)
        {
            startNode = FindNearestWalkableNode(startNode);
        }

        if (targetNode == null || startNode == null)
        {
            return null;
        }

        List<PathNode> openSet = new List<PathNode>();
        HashSet<PathNode> closedSet = new HashSet<PathNode>();

        startNode.gCost = 0;
        startNode.hCost = GetDistance(startNode, targetNode);

        openSet.Add(startNode);

        while (openSet.Count > 0)
        {
            PathNode currentNode = openSet[0];

            for (int i = 1; i < openSet.Count; i++)
            {
                if (openSet[i].fCost < currentNode.fCost ||
                    openSet[i].fCost == currentNode.fCost && openSet[i].hCost < currentNode.hCost)
                {
                    currentNode = openSet[i];
                }
            }

            openSet.Remove(currentNode);
            closedSet.Add(currentNode);

            if (currentNode == targetNode)
            {
                return RetracePath(startNode, targetNode);
            }

            foreach (PathNode neighbor in GetNeighbors(currentNode))
            {
                if (!neighbor.walkable || closedSet.Contains(neighbor))
                {
                    continue;
                }

                int newMovementCost = currentNode.gCost + GetDistance(currentNode, neighbor);

                if (newMovementCost < neighbor.gCost || !openSet.Contains(neighbor))
                {
                    neighbor.gCost = newMovementCost;
                    neighbor.hCost = GetDistance(neighbor, targetNode);
                    neighbor.parent = currentNode;

                    if (!openSet.Contains(neighbor))
                    {
                        openSet.Add(neighbor);
                    }
                }
            }
        }

        return null;
    }

    public bool IsInsideGrid(Vector3 position)
    {
        Vector3 bottomLeft = transform.position -
                             new Vector3(gridWidth / 2f, 0f, gridDepth / 2f);
        return position.x >= bottomLeft.x && position.x <= bottomLeft.x + gridWidth &&
               position.z >= bottomLeft.z && position.z <= bottomLeft.z + gridDepth;
    }

    public bool IsValidAgentPosition(Vector3 position, float clearanceRadius)
    {
        if (!IsInsideGrid(position))
        {
            return false;
        }

        PathNode node = NodeFromWorldPoint(position);
        return node != null && node.walkable &&
               !Physics.CheckSphere(
                   position + Vector3.up * 0.55f,
                   Mathf.Max(0.05f, clearanceRadius),
                   obstacleMask,
                   QueryTriggerInteraction.Ignore);
    }

    public bool TryGetNearestWalkablePosition(
        Vector3 requestedPosition,
        float maximumDistance,
        float clearanceRadius,
        out Vector3 walkablePosition)
    {
        if (grid == null)
        {
            CreateGrid();
        }

        PathNode best = null;
        float bestDistanceSquared = maximumDistance * maximumDistance;
        foreach (PathNode node in grid)
        {
            if (!node.walkable ||
                !IsValidAgentPosition(node.worldPosition, clearanceRadius))
            {
                continue;
            }

            Vector3 difference = node.worldPosition - requestedPosition;
            difference.y = 0f;
            float distanceSquared = difference.sqrMagnitude;
            if (distanceSquared <= bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                best = node;
            }
        }

        walkablePosition = best != null ? best.worldPosition : default;
        return best != null;
    }

    public bool TryGetNearestReachablePosition(
        Vector3 startPosition,
        Vector3 requestedPosition,
        float maximumAdjustment,
        float clearanceRadius,
        out Vector3 reachablePosition,
        out List<Vector3> path)
    {
        if (grid == null)
        {
            CreateGrid();
        }

        if (IsValidAgentPosition(requestedPosition, clearanceRadius))
        {
            List<Vector3> directPath = FindPath(startPosition, requestedPosition);
            if (directPath != null)
            {
                if (directPath.Count == 0 ||
                    FlatDistanceSquared(
                        directPath[directPath.Count - 1],
                        requestedPosition) > 0.04f)
                {
                    directPath.Add(requestedPosition);
                }

                reachablePosition = requestedPosition;
                path = directPath;
                return true;
            }
        }

        List<PathNode> candidates = new List<PathNode>();
        float maxDistanceSquared = maximumAdjustment * maximumAdjustment;
        foreach (PathNode node in grid)
        {
            if (!node.walkable ||
                !IsValidAgentPosition(node.worldPosition, clearanceRadius))
            {
                continue;
            }

            Vector3 difference = node.worldPosition - requestedPosition;
            difference.y = 0f;
            if (difference.sqrMagnitude <= maxDistanceSquared)
            {
                candidates.Add(node);
            }
        }

        candidates.Sort((left, right) =>
        {
            float leftDistance = FlatDistanceSquared(
                left.worldPosition,
                requestedPosition);
            float rightDistance = FlatDistanceSquared(
                right.worldPosition,
                requestedPosition);
            return leftDistance.CompareTo(rightDistance);
        });

        foreach (PathNode candidate in candidates)
        {
            List<Vector3> candidatePath = FindPath(
                startPosition,
                candidate.worldPosition);
            if (candidatePath == null)
            {
                continue;
            }

            reachablePosition = candidate.worldPosition;
            path = candidatePath;
            return true;
        }

        reachablePosition = default;
        path = null;
        return false;
    }

    private static float FlatDistanceSquared(Vector3 a, Vector3 b)
    {
        float x = a.x - b.x;
        float z = a.z - b.z;
        return x * x + z * z;
    }

    public bool TryGetNearestWalkablePositionInBounds(
        Vector3 requestedPosition,
        Bounds allowedBounds,
        out Vector3 walkablePosition)
    {
        return TryGetNearestWalkablePositionInBounds(
            requestedPosition,
            allowedBounds,
            obstacleCheckRadius,
            out walkablePosition);
    }

    public bool TryGetNearestWalkablePositionInBounds(
        Vector3 requestedPosition,
        Bounds allowedBounds,
        float clearanceRadius,
        out Vector3 walkablePosition)
    {
        if (grid == null)
        {
            CreateGrid();
        }

        PathNode nearestNode = null;
        float nearestDistanceSquared = Mathf.Infinity;

        foreach (PathNode node in grid)
        {
            if (!node.walkable ||
                !IsValidAgentPosition(node.worldPosition, clearanceRadius))
            {
                continue;
            }

            Vector3 position = node.worldPosition;
            bool insideXZ = position.x >= allowedBounds.min.x &&
                            position.x <= allowedBounds.max.x &&
                            position.z >= allowedBounds.min.z &&
                            position.z <= allowedBounds.max.z;
            if (!insideXZ)
            {
                continue;
            }

            Vector2 difference = new Vector2(
                position.x - requestedPosition.x,
                position.z - requestedPosition.z);
            float distanceSquared = difference.sqrMagnitude;
            if (distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearestNode = node;
            }
        }

        if (nearestNode == null)
        {
            walkablePosition = default;
            return false;
        }

        walkablePosition = nearestNode.worldPosition;
        return true;
    }

    private void ResetNodes()
    {
        for (int x = 0; x < gridSizeX; x++)
        {
            for (int z = 0; z < gridSizeZ; z++)
            {
                grid[x, z].gCost = int.MaxValue;
                grid[x, z].hCost = 0;
                grid[x, z].parent = null;
            }
        }
    }

    private List<Vector3> RetracePath(PathNode startNode, PathNode endNode)
    {
        List<Vector3> path = new List<Vector3>();
        PathNode currentNode = endNode;

        while (currentNode != startNode)
        {
            path.Add(currentNode.worldPosition);
            currentNode = currentNode.parent;

            if (currentNode == null)
            {
                return null;
            }
        }

        path.Reverse();
        return path;
    }

    private PathNode NodeFromWorldPoint(Vector3 worldPosition)
    {
        float percentX = (worldPosition.x - (transform.position.x - gridWidth / 2f)) / gridWidth;
        float percentZ = (worldPosition.z - (transform.position.z - gridDepth / 2f)) / gridDepth;

        percentX = Mathf.Clamp01(percentX);
        percentZ = Mathf.Clamp01(percentZ);

        int x = Mathf.Clamp(Mathf.RoundToInt((gridSizeX - 1) * percentX), 0, gridSizeX - 1);
        int z = Mathf.Clamp(Mathf.RoundToInt((gridSizeZ - 1) * percentZ), 0, gridSizeZ - 1);

        return grid[x, z];
    }

    private PathNode FindNearestWalkableNode(PathNode centerNode)
    {
        int maxSearchDistance = 8;

        for (int distance = 1; distance <= maxSearchDistance; distance++)
        {
            for (int x = -distance; x <= distance; x++)
            {
                for (int z = -distance; z <= distance; z++)
                {
                    int checkX = centerNode.gridX + x;
                    int checkZ = centerNode.gridZ + z;

                    if (checkX < 0 || checkX >= gridSizeX || checkZ < 0 || checkZ >= gridSizeZ)
                    {
                        continue;
                    }

                    PathNode node = grid[checkX, checkZ];

                    if (node.walkable)
                    {
                        return node;
                    }
                }
            }
        }

        return null;
    }

    private List<PathNode> GetNeighbors(PathNode node)
    {
        List<PathNode> neighbors = new List<PathNode>();

        for (int x = -1; x <= 1; x++)
        {
            for (int z = -1; z <= 1; z++)
            {
                if (x == 0 && z == 0)
                {
                    continue;
                }

                int checkX = node.gridX + x;
                int checkZ = node.gridZ + z;

                if (checkX < 0 || checkX >= gridSizeX || checkZ < 0 || checkZ >= gridSizeZ)
                {
                    continue;
                }

                if (Mathf.Abs(x) == 1 && Mathf.Abs(z) == 1)
                {
                    PathNode sideNodeA = grid[node.gridX + x, node.gridZ];
                    PathNode sideNodeB = grid[node.gridX, node.gridZ + z];

                    if (!sideNodeA.walkable || !sideNodeB.walkable)
                    {
                        continue;
                    }
                }

                neighbors.Add(grid[checkX, checkZ]);
            }
        }

        return neighbors;
    }

    private int GetDistance(PathNode nodeA, PathNode nodeB)
    {
        int distanceX = Mathf.Abs(nodeA.gridX - nodeB.gridX);
        int distanceZ = Mathf.Abs(nodeA.gridZ - nodeB.gridZ);

        if (distanceX > distanceZ)
        {
            return 14 * distanceZ + 10 * (distanceX - distanceZ);
        }

        return 14 * distanceX + 10 * (distanceZ - distanceX);
    }

    private void OnDrawGizmos()
    {
        if (!drawGrid || grid == null)
        {
            return;
        }

        foreach (PathNode node in grid)
        {
            Gizmos.color = node.walkable
                ? new Color(0f, 1f, 0f, 0.15f)
                : new Color(1f, 0f, 0f, 0.45f);

            Vector3 drawPosition = node.worldPosition + Vector3.up * gizmoHeight;
            Gizmos.DrawCube(drawPosition, new Vector3(cellSize * 0.85f, 0.02f, cellSize * 0.85f));
        }
    }

    private class PathNode
    {
        public bool walkable;
        public Vector3 worldPosition;
        public int gridX;
        public int gridZ;

        public int gCost;
        public int hCost;
        public PathNode parent;

        public int fCost => gCost + hCost;

        public PathNode(bool walkable, Vector3 worldPosition, int gridX, int gridZ)
        {
            this.walkable = walkable;
            this.worldPosition = worldPosition;
            this.gridX = gridX;
            this.gridZ = gridZ;

            gCost = int.MaxValue;
            hCost = 0;
            parent = null;
        }
    }
}
