using System.Collections.Generic;
using System.Linq;

namespace Smapshot.Helpers
{
    /// <summary>
    /// Represents a connectivity graph of roads for orphan detection.
    /// </summary>
    public class RoadNetworkGraph
    {
        public class RoadInfo
        {
            public int Index; // Index in the original roads list
            public List<long> NodeIds;
            public string Highway;
            public string? Name;
            public bool IsAnchor;
            public bool IsConnected;
        }

        private readonly List<RoadInfo> _roads;
        private readonly Dictionary<long, List<int>> _nodeToRoads;

        public RoadNetworkGraph(List<(List<long> nodeIds, string highway, string? name)> roads, HashSet<long> nodesInPolygon)
        {
            _roads = new List<RoadInfo>();
            _nodeToRoads = new Dictionary<long, List<int>>();
            for (int i = 0; i < roads.Count; i++)
            {
                var (nodeIds, highway, name) = roads[i];
                // Only keep nodes inside the polygon for connectivity
                var filteredNodeIds = nodeIds.Where(nodesInPolygon.Contains).ToList();
                var info = new RoadInfo
                {
                    Index = i,
                    NodeIds = filteredNodeIds,
                    Highway = highway,
                    Name = name,
                    IsAnchor = highway == "motorway" || highway == "trunk" || highway == "primary" || highway == "secondary",
                    IsConnected = false
                };
                _roads.Add(info);
                foreach (var nodeId in filteredNodeIds)
                {
                    if (!_nodeToRoads.ContainsKey(nodeId))
                        _nodeToRoads[nodeId] = new List<int>();
                    _nodeToRoads[nodeId].Add(i);
                }
            }
        }

        /// <summary>
        /// Marks all roads connected to anchors as IsConnected = true.
        /// </summary>
        public void MarkConnectedRoads()
        {
            var visited = new HashSet<int>();
            var queue = new Queue<int>();
            // Start from all anchor roads
            foreach (var road in _roads.Where(r => r.IsAnchor))
            {
                queue.Enqueue(road.Index);
                visited.Add(road.Index);
                road.IsConnected = true;
            }
            while (queue.Count > 0)
            {
                int idx = queue.Dequeue();
                var road = _roads[idx];
                foreach (var nodeId in road.NodeIds)
                {
                    foreach (var neighborIdx in _nodeToRoads[nodeId])
                    {
                        if (!visited.Contains(neighborIdx))
                        {
                            visited.Add(neighborIdx);
                            _roads[neighborIdx].IsConnected = true;
                            queue.Enqueue(neighborIdx);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Returns the indices of roads that are connected to anchors.
        /// </summary>
        public HashSet<int> GetConnectedRoadIndices()
        {
            return _roads.Where(r => r.IsConnected).Select(r => r.Index).ToHashSet();
        }

        /// <summary>
        /// Returns the indices of roads in the largest connected component (main network).
        /// </summary>
        public HashSet<int> GetLargestConnectedComponent()
        {
            var visited = new HashSet<int>();
            var largestComponent = new HashSet<int>();
            for (int i = 0; i < _roads.Count; i++)
            {
                if (visited.Contains(i) || _roads[i].NodeIds.Count == 0)
                    continue;
                var component = new HashSet<int>();
                var queue = new Queue<int>();
                queue.Enqueue(i);
                visited.Add(i);
                component.Add(i);
                while (queue.Count > 0)
                {
                    int idx = queue.Dequeue();
                    foreach (var nodeId in _roads[idx].NodeIds)
                    {
                        foreach (var neighborIdx in _nodeToRoads[nodeId])
                        {
                            if (!visited.Contains(neighborIdx))
                            {
                                visited.Add(neighborIdx);
                                component.Add(neighborIdx);
                                queue.Enqueue(neighborIdx);
                            }
                        }
                    }
                }
                if (component.Count > largestComponent.Count)
                    largestComponent = component;
            }
            return largestComponent;
        }

        /// <summary>
        /// Returns the indices of roads in the largest connected component that contains at least one anchor (major) road.
        /// </summary>
        public HashSet<int> GetMainNetworkComponent()
        {
            var visited = new HashSet<int>();
            HashSet<int> bestComponent = new();
            int bestCount = 0;
            for (int i = 0; i < _roads.Count; i++)
            {
                if (visited.Contains(i) || _roads[i].NodeIds.Count == 0)
                    continue;
                var component = new HashSet<int>();
                bool hasAnchor = false;
                var queue = new Queue<int>();
                queue.Enqueue(i);
                visited.Add(i);
                component.Add(i);
                if (_roads[i].IsAnchor) hasAnchor = true;
                while (queue.Count > 0)
                {
                    int idx = queue.Dequeue();
                    foreach (var nodeId in _roads[idx].NodeIds)
                    {
                        foreach (var neighborIdx in _nodeToRoads[nodeId])
                        {
                            if (!visited.Contains(neighborIdx))
                            {
                                visited.Add(neighborIdx);
                                component.Add(neighborIdx);
                                if (_roads[neighborIdx].IsAnchor) hasAnchor = true;
                                queue.Enqueue(neighborIdx);
                            }
                        }
                    }
                }
                if (hasAnchor && component.Count > bestCount)
                {
                    bestComponent = component;
                    bestCount = component.Count;
                }
            }
            return bestComponent;
        }
    }
}
