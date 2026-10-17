using System.Collections.Generic;
using UnityEngine;

namespace Game.Characters.Modular
{
    /// <summary>
    /// Checks the rig contract from decision 0001: a part's skeleton must match the body's (same bone names,
    /// same rest pose), or rebinding it onto the body distorts it. Part-only bones (hair tails) are allowed.
    /// </summary>
    public static class RigCheck
    {
        public static List<string> Compare(Transform bodyRoot, Transform partRoot, float positionTolerance = 1e-4f, float angleTolerance = 0.05f)
        {
            var problems = new List<string>();
            var body = new Dictionary<string, Transform>();
            foreach (Transform bone in bodyRoot.GetComponentsInChildren<Transform>(true)) body[bone.name] = bone;

            foreach (Transform bone in partRoot.GetComponentsInChildren<Transform>(true))
            {
                if (bone == partRoot || !body.TryGetValue(bone.name, out Transform match)) continue;
                Vector3 partPosition = partRoot.InverseTransformPoint(bone.position);
                Vector3 bodyPosition = bodyRoot.InverseTransformPoint(match.position);
                if (Vector3.Distance(partPosition, bodyPosition) > positionTolerance)
                    problems.Add($"'{bone.name}' rests at {partPosition:F4} in the part but {bodyPosition:F4} in the body.");
                float angle = Quaternion.Angle(Quaternion.Inverse(partRoot.rotation) * bone.rotation, Quaternion.Inverse(bodyRoot.rotation) * match.rotation);
                if (angle > angleTolerance)
                    problems.Add($"'{bone.name}' is rotated {angle:F2}° differently in the part.");
            }
            return problems;
        }
    }
}
