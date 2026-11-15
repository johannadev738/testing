using System;
using UnityEngine;

namespace SimSafe
{
    /// <summary>
    /// A simple procedural humanoid built from primitives and animated in code (no imported rig).
    /// It is a stand-in so the prototype is playable; swap in licensed, motion-captured characters
    /// for anything beyond a prototype.
    ///
    /// Body language here is driven by scenario state (covering ears, pacing, sitting, standing calmly),
    /// not by a fixed "autistic" animation set: autistic people respond in many different ways.
    /// </summary>
    public sealed class Humanoid : MonoBehaviour
    {
        Transform rig, torso, shoulderL, shoulderR, elbowL, elbowR, hipL, hipR;

        public bool CoverEars;
        public bool Sit;
        public Transform FaceTarget;

        Vector3[] route;
        int routeIndex;
        bool routeLoop;
        float speed = 1.2f;
        Action arrived;

        float phase, walkBlend, sitBlend, earsBlend, clock;

        public bool IsMoving { get; private set; }

        public static Humanoid Create(Transform parent, string name, Vector3 position, Color skin, Color shirt, Color pants, Color hair)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            var h = root.AddComponent<Humanoid>();
            h.Build(skin, shirt, pants, hair);
            var col = root.AddComponent<CapsuleCollider>();
            col.height = 1.8f; col.radius = 0.32f; col.center = new Vector3(0f, 0.9f, 0f);
            return h;
        }

        static Transform Pivot(Transform parent, string name, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        void Build(Color skin, Color shirt, Color pants, Color hair)
        {
            var mSkin = Gfx.Lit(skin, 0.3f);
            var mShirt = Gfx.Lit(shirt, 0.2f);
            var mPants = Gfx.Lit(pants, 0.15f);
            var mHair = Gfx.Lit(hair, 0.25f);
            var mShoe = Gfx.Lit(new Color(0.08f, 0.08f, 0.09f), 0.2f);

            rig = Pivot(transform, "Rig", Vector3.zero);
            torso = Gfx.Prim(PrimitiveType.Capsule, rig, "Torso", new Vector3(0f, 1.2f, 0f), new Vector3(0.36f, 0.33f, 0.22f), mShirt, false).transform;
            Gfx.Ball(rig, "Head", new Vector3(0f, 1.62f, 0f), 0.23f, mSkin);
            Gfx.Prim(PrimitiveType.Sphere, rig, "Hair", new Vector3(0f, 1.69f, -0.01f), new Vector3(0.245f, 0.15f, 0.25f), mHair, false);

            hipL = Leg(-0.1f, mPants, mShoe);
            hipR = Leg(0.1f, mPants, mShoe);
            shoulderL = Arm(-0.24f, mShirt, mSkin, out elbowL);
            shoulderR = Arm(0.24f, mShirt, mSkin, out elbowR);
        }

        Transform Leg(float x, Material pants, Material shoe)
        {
            var hip = Pivot(rig, "Hip", new Vector3(x, 0.92f, 0f));
            Gfx.Prim(PrimitiveType.Capsule, hip, "Leg", new Vector3(0f, -0.45f, 0f), new Vector3(0.19f, 0.45f, 0.19f), pants, false);
            Gfx.Prim(PrimitiveType.Cube, hip, "Shoe", new Vector3(0f, -0.9f, 0.05f), new Vector3(0.17f, 0.08f, 0.29f), shoe, false);
            return hip;
        }

        Transform Arm(float x, Material shirt, Material skin, out Transform elbow)
        {
            var shoulder = Pivot(rig, "Shoulder", new Vector3(x, 1.46f, 0f));
            Gfx.Prim(PrimitiveType.Capsule, shoulder, "Upper", new Vector3(0f, -0.15f, 0f), new Vector3(0.1f, 0.15f, 0.1f), shirt, false);
            elbow = Pivot(shoulder, "Elbow", new Vector3(0f, -0.3f, 0f));
            Gfx.Prim(PrimitiveType.Capsule, elbow, "Fore", new Vector3(0f, -0.14f, 0f), new Vector3(0.085f, 0.14f, 0.085f), skin, false);
            Gfx.Ball(elbow, "Hand", new Vector3(0f, -0.29f, 0f), 0.09f, skin);
            return shoulder;
        }

        // ---- movement API -------------------------------------------------

        public void Teleport(Vector3 position)
        {
            transform.position = position;
            route = null; IsMoving = false; arrived = null;
        }

        public void MoveTo(Vector3[] points, float metresPerSecond, bool loop, Action onArrive = null)
        {
            route = points; routeIndex = 0; routeLoop = loop; speed = metresPerSecond; arrived = onArrive;
        }

        public void Stop() { route = null; arrived = null; }

        public void SetPose(bool coverEars, bool sit) { CoverEars = coverEars; Sit = sit; }

        // ---- animation ----------------------------------------------------

        void Update()
        {
            float dt = Time.deltaTime;
            clock += dt;
            bool moving = false;

            if (route != null && route.Length > 0)
            {
                Vector3 to = route[routeIndex] - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude < 0.04f)
                {
                    if (routeIndex < route.Length - 1) routeIndex++;
                    else if (routeLoop) routeIndex = 0;
                    else
                    {
                        var p = route[routeIndex]; transform.position = new Vector3(p.x, p.y, p.z);
                        route = null;
                        var cb = arrived; arrived = null;
                        if (cb != null) cb();
                    }
                }
                else
                {
                    Vector3 dir = to.normalized;
                    var pos = transform.position + dir * speed * dt;
                    pos.y = Mathf.MoveTowards(pos.y, route[routeIndex].y, dt);   // step up onto the porch
                    transform.position = pos;
                    Face(dir, dt);
                    moving = true;
                }
            }
            else if (FaceTarget != null)
            {
                Vector3 d = FaceTarget.position - transform.position; d.y = 0f;
                Face(d, dt);
            }

            IsMoving = moving;
            walkBlend = Mathf.MoveTowards(walkBlend, moving ? 1f : 0f, dt * 4f);
            sitBlend = Mathf.MoveTowards(sitBlend, Sit ? 1f : 0f, dt * 3f);
            earsBlend = Mathf.MoveTowards(earsBlend, CoverEars ? 1f : 0f, dt * 2.5f);
            if (moving) phase += speed * 3.4f * dt;

            float swing = Mathf.Sin(phase) * 32f * walkBlend;
            float legL = Mathf.Lerp(swing, -85f, sitBlend);
            float legR = Mathf.Lerp(-swing, -85f, sitBlend);
            hipL.localRotation = Quaternion.Euler(legL, 0f, 0f);
            hipR.localRotation = Quaternion.Euler(legR, 0f, 0f);
            rig.localPosition = new Vector3(0f, -0.45f * sitBlend, 0f);

            float breathe = 1f + 0.012f * Mathf.Sin(clock * 1.6f);
            torso.localScale = new Vector3(0.36f, 0.33f * breathe, 0.22f);

            // Arms hang with a small swing, or rise to cover the ears (left arm mirrored).
            var restL = Quaternion.Euler(-swing * 0.8f, 0f, -3f);
            var restR = Quaternion.Euler(swing * 0.8f, 0f, 3f);
            shoulderL.localRotation = Quaternion.Slerp(restL, Quaternion.Euler(0f, 0f, -125f), earsBlend);
            shoulderR.localRotation = Quaternion.Slerp(restR, Quaternion.Euler(0f, 0f, 125f), earsBlend);
            elbowL.localRotation = Quaternion.Slerp(Quaternion.Euler(-10f, 0f, 0f), Quaternion.Euler(0f, 0f, -115f), earsBlend);
            elbowR.localRotation = Quaternion.Slerp(Quaternion.Euler(-10f, 0f, 0f), Quaternion.Euler(0f, 0f, 115f), earsBlend);
        }

        void Face(Vector3 dir, float dt)
        {
            if (dir.sqrMagnitude < 0.0001f) return;
            var target = Quaternion.LookRotation(dir.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-8f * dt));
        }
    }
}
