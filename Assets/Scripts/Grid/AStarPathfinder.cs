using System.Collections.Generic;
using UnityEngine;

public class AStarPathfinder : MonoBehaviour
{
    public static AStarPathfinder Instance { get; private set; }

    [Header("Grid Settings")]
    public float gridWidth = 16f;
    public float gridHeight = 10f;
    public float cellSize = 0.5f;

    [Header("Obstacle Settings")]
    public LayerMask obstacleMask;
    [Tooltip("Wall clearance. Keep this at least as large as half the widest agent collider in world units.")]
    public float obstacleCheckRadius = 0.5f;
    [Tooltip("Cost penalty for nodes cardinally adjacent to walls to keep agents centered.")]
    public int wallProximityPenalty = 4;
    [Tooltip("Cost penalty for nodes 2 cells away from walls (including diagonals).")]
    public int wallBufferPenalty = 1;

    [Header("Debug")]
    public bool drawGrid = true;

    private PathNode[,] grid;
    private int gridSizeX;
    private int gridSizeY;

    public int GridSizeX => gridSizeX;
    public int GridSizeY => gridSizeY;

    private void Awake()
    {
        Instance = this;
        CreateGrid();
    }

    private void CreateGrid()
    {
        gridSizeX = Mathf.RoundToInt(gridWidth / cellSize);
        gridSizeY = Mathf.RoundToInt(gridHeight / cellSize);

        grid = new PathNode[gridSizeX, gridSizeY];

        Vector2 bottomLeft = (Vector2)transform.position - new Vector2(gridWidth / 2f, gridHeight / 2f);

        for (int x = 0; x < gridSizeX; x++)
        {
            for (int y = 0; y < gridSizeY; y++)
            {
                Vector2 worldPosition = bottomLeft + new Vector2(
                    x * cellSize + cellSize / 2f,
                    y * cellSize + cellSize / 2f
                );

                bool blocked = Physics2D.OverlapCircle(worldPosition, obstacleCheckRadius, obstacleMask);
                bool walkable = !blocked;

                grid[x, y] = new PathNode(walkable, worldPosition, x, y);
            }
        }
    }

    public bool IsWalkable(int x, int y)
    {
        return grid != null && x >= 0 && x < gridSizeX && y >= 0 && y < gridSizeY
            && grid[x, y].walkable;
    }

    public Vector2 GridToWorld(int x, int y)
    {
        if (grid == null || x < 0 || x >= gridSizeX || y < 0 || y >= gridSizeY)
        {
            return transform.position;
        }

        return grid[x, y].worldPosition;
    }

    public bool TryGetRandomWalkablePosition(
        Vector2 origin,
        float minimumDistance,
        out Vector2 position)
    {
        if (grid == null)
        {
            CreateGrid();
        }

        const int maximumAttempts = 100;
        for (int attempt = 0; attempt < maximumAttempts; attempt++)
        {
            int x = Random.Range(0, gridSizeX);
            int y = Random.Range(0, gridSizeY);
            PathNode candidate = grid[x, y];

            if (candidate.walkable &&
                Vector2.Distance(origin, candidate.worldPosition) >= minimumDistance)
            {
                position = candidate.worldPosition;
                return true;
            }
        }

        position = origin;
        return false;
    }

    public List<Vector2> FindPath(Vector2 startWorldPosition, Vector2 targetWorldPosition)
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

                int newMovementCost =
                    currentNode.gCost +
                    GetDistance(currentNode, neighbor) +
                    GetWallPenalty(neighbor);

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

    private void ResetNodes()
    {
        for (int x = 0; x < gridSizeX; x++)
        {
            for (int y = 0; y < gridSizeY; y++)
            {
                grid[x, y].gCost = int.MaxValue;
                grid[x, y].hCost = 0;
                grid[x, y].parent = null;
            }
        }
    }

    private List<Vector2> RetracePath(PathNode startNode, PathNode endNode)
    {
        List<Vector2> path = new List<Vector2>();
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

    private PathNode NodeFromWorldPoint(Vector2 worldPosition)
    {
        float percentX = (worldPosition.x - (transform.position.x - gridWidth / 2f)) / gridWidth;
        float percentY = (worldPosition.y - (transform.position.y - gridHeight / 2f)) / gridHeight;

        percentX = Mathf.Clamp01(percentX);
        percentY = Mathf.Clamp01(percentY);

        int x = Mathf.Clamp(Mathf.RoundToInt((gridSizeX - 1) * percentX), 0, gridSizeX - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt((gridSizeY - 1) * percentY), 0, gridSizeY - 1);

        return grid[x, y];
    }

    private PathNode FindNearestWalkableNode(PathNode centerNode)
    {
        int maxSearchDistance = 6;

        for (int distance = 1; distance <= maxSearchDistance; distance++)
        {
            for (int x = -distance; x <= distance; x++)
            {
                for (int y = -distance; y <= distance; y++)
                {
                    int checkX = centerNode.gridX + x;
                    int checkY = centerNode.gridY + y;

                    if (checkX < 0 || checkX >= gridSizeX || checkY < 0 || checkY >= gridSizeY)
                    {
                        continue;
                    }

                    PathNode node = grid[checkX, checkY];

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

        int[,] directions =
        {
            { 0, 1 },
            { 1, 0 },
            { 0, -1 },
            { -1, 0 }
        };

        for (int i = 0; i < directions.GetLength(0); i++)
        {
            int checkX = node.gridX + directions[i, 0];
            int checkY = node.gridY + directions[i, 1];

            if (checkX >= 0 && checkX < gridSizeX && checkY >= 0 && checkY < gridSizeY)
            {
                neighbors.Add(grid[checkX, checkY]);
            }
        }

        return neighbors;
    }

    private int GetDistance(PathNode nodeA, PathNode nodeB)
    {
        int distanceX = Mathf.Abs(nodeA.gridX - nodeB.gridX);
        int distanceY = Mathf.Abs(nodeA.gridY - nodeB.gridY);

        return distanceX + distanceY;
    }

    private int GetWallPenalty(PathNode node)
    {
        if (HasBlockedNeighbor(node, 1, false))
        {
            return wallProximityPenalty;
        }

        if (HasBlockedNeighbor(node, 2, true))
        {
            return wallBufferPenalty;
        }

        return 0;
    }

    private bool HasBlockedNeighbor(PathNode node, int distance, bool includeDiagonals)
    {
        for (int offsetX = -distance; offsetX <= distance; offsetX++)
        {
            for (int offsetY = -distance; offsetY <= distance; offsetY++)
            {
                if (offsetX == 0 && offsetY == 0)
                {
                    continue;
                }

                if (!includeDiagonals && Mathf.Abs(offsetX) + Mathf.Abs(offsetY) != distance)
                {
                    continue;
                }

                int checkX = node.gridX + offsetX;
                int checkY = node.gridY + offsetY;

                // Treat map edges like walls so paths remain inside the playable area.
                if (checkX < 0 || checkX >= gridSizeX || checkY < 0 || checkY >= gridSizeY)
                {
                    return true;
                }

                if (!grid[checkX, checkY].walkable)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void OnDrawGizmos()
    {
        if (!drawGrid || grid == null)
        {
            return;
        }

        foreach (PathNode node in grid)
        {
            Gizmos.color = node.walkable ? new Color(0f, 1f, 0f, 0.15f) : new Color(1f, 0f, 0f, 0.5f);
            Gizmos.DrawCube(node.worldPosition, Vector3.one * (cellSize * 0.8f));
        }
    }

    private class PathNode
    {
        public bool walkable;
        public Vector2 worldPosition;
        public int gridX;
        public int gridY;

        public int gCost;
        public int hCost;
        public PathNode parent;

        public int fCost => gCost + hCost;

        public PathNode(bool walkable, Vector2 worldPosition, int gridX, int gridY)
        {
            this.walkable = walkable;
            this.worldPosition = worldPosition;
            this.gridX = gridX;
            this.gridY = gridY;

            gCost = int.MaxValue;
            hCost = 0;
            parent = null;
        }
    }
}
