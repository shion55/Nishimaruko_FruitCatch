using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public sealed class BananaPeel : MonoBehaviour
{
    private readonly List<Collider2D> overlaps = new List<Collider2D>();
    private ItemEffectController owner;
    private Collider2D carrier;
    private BoxCollider2D sensor;
    private Vector3 from;
    private Vector3 destination;
    private float flightDuration;
    private float flightTime;
    private float arcHeight;
    private int direction;
    private bool used;
    public bool HasLanded { get; private set; }

    public void Launch(ItemEffectController owner, Collider2D carrier, Vector3 destination,
        int direction, float duration, float height)
    {
        this.owner = owner;
        this.carrier = carrier;
        this.destination = destination;
        this.direction = direction;
        from = transform.position;
        flightDuration = Mathf.Max(0.01f, duration);
        arcHeight = height;
        flightTime = 0f;
        HasLanded = used = false;
        sensor = GetComponent<BoxCollider2D>();
        sensor.isTrigger = true;
        sensor.enabled = false;
    }

    private void Update()
    {
        if (HasLanded || owner == null) return;
        flightTime += Time.deltaTime;
        float t = Mathf.Clamp01(flightTime / flightDuration);
        transform.position = Vector3.Lerp(from, destination, t) + Vector3.up * (4f * arcHeight * t * (1f - t));
        if (t >= 1f)
        {
            HasLanded = true;
            sensor.enabled = true;
        }
    }

    private void FixedUpdate()
    {
        if (!HasLanded || used || carrier == null || owner == null) return;
        sensor.Overlap(ContactFilter2D.noFilter, overlaps);
        if (overlaps.Contains(carrier))
        {
            used = true;
            sensor.enabled = false;
            owner.StepOnPeel(this, direction);
        }
    }
}
