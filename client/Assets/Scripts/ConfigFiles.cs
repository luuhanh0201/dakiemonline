using System;
using System.Collections;
using Game.Rules;
using UnityEngine;
using UnityEngine.Networking;

namespace DaKiem.Client
{
    // Temporary reader for T010. T013 replaces it with the validating loader in Game.Rules.
    public static class ConfigFiles
    {
        public static IEnumerator Load(string fileName, Action<string> onLoaded)
        {
            string path = $"{Application.streamingAssetsPath}/config/{fileName}";
            string url = path.Contains("://") ? path : $"file://{path}";

            using UnityWebRequest request = UnityWebRequest.Get(url);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new InvalidOperationException($"Cannot read {fileName}: {request.error}");
            }

            onLoaded(request.downloadHandler.text);
        }

        public static MovementConfig ParseMovement(string json)
        {
            MovementFile file = JsonUtility.FromJson<MovementFile>(json);

            return new MovementConfig
            {
                RunSpeed = file.runSpeed,
                WalkSpeed = file.walkSpeed,
                WalkTiltBelow = file.walkTiltBelow,
                JumpHeight = file.jumpHeight,
                JumpTimeToPeakSec = file.jumpTimeToPeakSec,
                JumpSwipeAbove = file.jumpSwipeAbove,
                CoyoteTimeSec = file.coyoteTimeSec,
                InputBufferSec = file.inputBufferSec,
                DashDistance = file.dashDistance,
                DashDurationSec = file.dashDurationSec,
                DashStaminaCost = file.dashStaminaCost,
                AirDashesPerJump = file.airDashesPerJump,
                BodyWidth = file.bodyWidth,
                BodyHeight = file.bodyHeight,
            };
        }

        public static MapBox[] ParseMap(string json)
        {
            MapFile file = JsonUtility.FromJson<MapFile>(json);
            var boxes = new MapBox[file.boxes.Length];

            for (int index = 0; index < boxes.Length; index++)
            {
                MapBoxFile box = file.boxes[index];
                boxes[index] = new MapBox
                {
                    Id = box.id,
                    MinX = box.minX,
                    MinY = box.minY,
                    MaxX = box.maxX,
                    MaxY = box.maxY,
                    OneWay = box.oneWay,
                };
            }

            return boxes;
        }

        [Serializable]
        private sealed class MovementFile
        {
            public double runSpeed;
            public double walkSpeed;
            public double walkTiltBelow;
            public double jumpHeight;
            public double jumpTimeToPeakSec;
            public double jumpSwipeAbove;
            public double coyoteTimeSec;
            public double inputBufferSec;
            public double dashDistance;
            public double dashDurationSec;
            public double dashStaminaCost;
            public int airDashesPerJump;
            public double bodyWidth;
            public double bodyHeight;
        }

        [Serializable]
        private sealed class MapFile
        {
            public MapBoxFile[] boxes;
        }

        [Serializable]
        private sealed class MapBoxFile
        {
            public string id;
            public double minX;
            public double minY;
            public double maxX;
            public double maxY;
            public bool oneWay;
        }
    }
}
