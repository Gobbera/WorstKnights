using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;

public static partial class FlamethrowerTrapAuthoringUtility
{
    public const string SurfaceGraphPath = "Assets/VFX/Flamethrower/Flamethrower_SurfaceSpread.vfx";

    public static void PrepareSurfaceGraph()
    {
        if (AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(SurfaceGraphPath) == null && !AssetDatabase.CopyAsset(GraphPath, SurfaceGraphPath))
            throw new InvalidOperationException("Nao foi possivel criar o VFX de contato.");
        var asset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(SurfaceGraphPath);
        object resource = Extension("GetResource", asset);
        object graph = Extension("GetOrCreateGraph", resource);
        ((UnityEngine.Object)graph).name = "Flamethrower Surface Spread";
        var parameters = new Dictionary<string, object>();
        void Add(string name, Type type, object value) => parameters[name] = Parameter(graph, name, type, value);
        Add("Surface Position", typeof(Vector3), Vector3.zero);
        Add("Surface Normal", typeof(Vector3), Vector3.forward);
        Add("Surface Tangent", typeof(Vector3), Vector3.right);
        Add("Surface Bitangent", typeof(Vector3), Vector3.up);
        Add("Surface Incident", typeof(Vector3), Vector3.back);
        Add("Surface Speed", typeof(float), 9f);
        Add("Surface Retention", typeof(float), 0.45f);
        Add("Surface Reflection", typeof(float), 0.12f);
        Add("Surface Friction", typeof(float), 0.35f);
        Add("Surface Drag", typeof(float), 2f);
        Add("Surface Buoyancy", typeof(float), 0.4f);
        Add("Surface Lifetime", typeof(float), 1.2f);
        Add("Surface Fade", typeof(float), 1.5f);
        Add("Surface Scale", typeof(float), 0.45f);
        Add("Surface Rate", typeof(float), 30f);
        Add("Surface Limits A", typeof(Vector4), Vector4.one * 2.5f);
        Add("Surface Limits B", typeof(Vector4), Vector4.one * 2.5f);
        foreach (var pair in new[] { ("Jet Bounds Center", "Surface Bounds Center", Vector3.zero), ("Jet Bounds Size", "Surface Bounds Size", Vector3.one * 9f) })
        {
            object old = Children(graph).FirstOrDefault(c => c.GetType() == TypeOf("VFXParameter") && Convert.ToString(Get(c, "exposedName")) == pair.Item1);
            if (old != null) Setting(old, "m_ExposedName", pair.Item2);
            Add(pair.Item2, typeof(Vector3), pair.Item3);
        }
        object delta = Children(graph).FirstOrDefault(c => c.GetType() == TypeOf("VFXDynamicBuiltInParameter"));
        if (delta == null)
        {
            delta = ScriptableObject.CreateInstance(TypeOf("VFXDynamicBuiltInParameter"));
            var field = delta.GetType().GetField("m_BuiltInParameters", Flags);
            Setting(delta, "m_BuiltInParameters", Enum.Parse(field.FieldType, "VfxDeltaTime"));
            Invoke(graph, "AddChild", delta, -1, true);
        }
        object[] contexts = Children(graph).Where(c => TypeOf("VFXContext").IsInstanceOfType(c)).ToArray();
        foreach (object context in contexts)
        {
            if (TypeOf("VFXBasicInitialize").IsInstanceOfType(context))
            {
                object data = Invoke(context, "GetData");
                var space = data.GetType().GetProperty("space", Flags);
                space.SetValue(data, Enum.Parse(space.PropertyType, "World"));
                foreach (object child in Children(context))
                    if (child.GetType() == TypeOf("Block.SetAttribute")
                        && new[] { "position", "velocity", "lifetime" }.Contains(Convert.ToString(Invoke(child, "GetSettingValue", "attribute"))))
                        RemoveModel(context, child);
                object bounds = Slots(context, "inputSlots")[0];
                foreach (object slot in Children(bounds))
                    Link(Slots(parameters[SlotName(slot) == "center" ? "Surface Bounds Center" : "Surface Bounds Size"], "outputSlots")[0], slot);
                object block = SurfaceBlock(context, "Surface Initial Deflection", SurfaceInitCode);
                Bind(block, parameters, ("position", "Surface Position"), ("normal", "Surface Normal"),
                    ("tangent", "Surface Tangent"), ("bitangent", "Surface Bitangent"), ("incident", "Surface Incident"),
                    ("speed", "Surface Speed"), ("retention", "Surface Retention"), ("reflection", "Surface Reflection"),
                    ("friction", "Surface Friction"), ("lifetime", "Surface Lifetime"));
            }
            else if (TypeOf("VFXBasicUpdate").IsInstanceOfType(context))
            {
                foreach (object child in Children(context))
                    if (child.GetType().FullName.EndsWith(".AttributeFromCurve", StringComparison.Ordinal)
                        && Convert.ToString(Invoke(child, "GetSettingValue", "attribute")) == "velocity") RemoveModel(context, child);
                object block = SurfaceBlock(context, "Surface Drag And Contact", SurfaceUpdateCode);
                Bind(block, parameters, ("position", "Surface Position"), ("normal", "Surface Normal"),
                    ("tangent", "Surface Tangent"), ("bitangent", "Surface Bitangent"), ("drag", "Surface Drag"),
                    ("buoyancy", "Surface Buoyancy"), ("reflection", "Surface Reflection"), ("friction", "Surface Friction"),
                    ("limitsA", "Surface Limits A"), ("limitsB", "Surface Limits B"));
                Link(Slots(delta, "outputSlots")[0], Slots(block, "inputSlots").First(s => SlotName(s) == "_dt"));
            }
            else if (TypeOf("VFXAbstractParticleOutput").IsInstanceOfType(context))
            {
                foreach (object child in Children(context))
                    if (child.GetType() == TypeOf("Block.CustomHLSL")
                        && Convert.ToString(Invoke(child, "GetSettingValue", "m_BlockName")) == "Trap Jet Shape") RemoveModel(context, child);
                object block = SurfaceBlock(context, "Surface Gradual Dissipation", SurfaceOutputCode);
                Bind(block, parameters, ("scale", "Surface Scale"), ("fade", "Surface Fade"));
            }
            else if (TypeOf("VFXBasicSpawner").IsInstanceOfType(context))
                foreach (object spawn in Children(context))
                    if (spawn.GetType() == TypeOf("VFXSpawnerConstantRate"))
                        Link(Slots(parameters["Surface Rate"], "outputSlots")[0], Slots(spawn, "inputSlots")[0]);
        }
        foreach (object model in Children(graph))
            if (model.GetType() == TypeOf("VFXParameter") && new[] { "Jet Range", "Jet Width" }.Contains(Convert.ToString(Get(model, "exposedName"))))
                RemoveModel(graph, model);
        PruneDetachedErrors(graph);
        Invoke(graph, "RecompileIfNeeded", false, false);
        Extension("WriteAssetWithSubAssets", resource);
        AssetDatabase.ImportAsset(SurfaceGraphPath, ImportAssetOptions.ForceUpdate);
    }

    static void RemoveModel(object parent, object model)
    {
        TypeOf("VFXModel").GetMethod("UnlinkModel", Flags).Invoke(null, new object[] { model, true });
        Invoke(parent, "RemoveChild", model, true);
    }
    static void PruneDetachedErrors(object graph)
    {
        // VFX 17 keeps removed blocks in its pending validation set during a batch edit.
        object manager = Get(graph, "errorManager");
        foreach (string name in new[] { "errorReporter", "compileReporter" })
        {
            object reporter = Get(manager, name);
            if (reporter == null) continue;
            var field = reporter.GetType().GetField("m_ScheduledModels", Flags);
            if (field == null) continue;
            object pending = field.GetValue(reporter);
            foreach (object model in ((IEnumerable)pending).Cast<object>().ToArray())
                if (Invoke(model, "GetGraph") == null) Invoke(pending, "Remove", model);
        }
    }
    static object SurfaceBlock(object context, string name, string code)
    {
        object block = Children(context).FirstOrDefault(c => c.GetType() == TypeOf("Block.CustomHLSL")
            && Convert.ToString(Invoke(c, "GetSettingValue", "m_BlockName")) == name);
        if (block == null)
        {
            block = ScriptableObject.CreateInstance(TypeOf("Block.CustomHLSL"));
            Setting(block, "m_BlockName", name);
            Invoke(context, "AddChild", block, -1, true);
        }
        Setting(block, "m_HLSLCode", code);
        Invoke(block, "ResyncSlots", true);
        return block;
    }
    static void Bind(object block, Dictionary<string, object> parameters, params (string slot, string parameter)[] bindings)
    {
        foreach (var binding in bindings)
            Link(Slots(parameters[binding.parameter], "outputSlots")[0], Slots(block, "inputSlots").First(s => SlotName(s) == "_" + binding.slot));
    }

    const string SurfaceInitCode = @"
void SurfaceInit(inout VFXAttributes attributes, in float3 position, in float3 normal,
    in float3 tangent, in float3 bitangent, in float3 incident, in float speed,
    in float retention, in float reflection, in float friction, in float lifetime)
{
    float2 random = VFXRAND2;
    float angle = random.x * 6.2831853;
    float3 radial = tangent * cos(angle) + bitangent * sin(angle);
    float inward = max(0.0, -dot(incident, normal));
    float3 sliding = incident - normal * dot(incident, normal);
    float3 outgoing = sliding * (1.0 - friction) + radial * inward * (1.0 - friction) + normal * inward * reflection;
    float energy = speed * retention * lerp(0.55, 1.0, random.y);
    attributes.position = position + normal * 0.06 + radial * 0.06;
    attributes.velocity = outgoing * (energy / max(1.0, length(outgoing)));
    attributes.lifetime = lifetime * lerp(0.65, 1.0, VFXRAND);
}
";
    const string SurfaceUpdateCode = @"
void SurfaceFlow(inout VFXAttributes attributes, in float3 position, in float3 normal,
    in float3 tangent, in float3 bitangent, in float drag, in float buoyancy,
    in float reflection, in float friction, in float4 limitsA, in float4 limitsB, in float dt)
{
    attributes.velocity *= exp(-max(0.0, drag) * dt);
    attributes.velocity += float3(0.0, buoyancy, 0.0) * dt;
    float height = dot(attributes.position - position, normal);
    if (height < 0.06)
    {
        attributes.position += normal * (0.06 - height);
        float vn = dot(attributes.velocity, normal);
        if (vn < 0.0) attributes.velocity -= normal * ((1.0 + reflection) * vn);
        float3 normalVelocity = normal * dot(attributes.velocity, normal);
        attributes.velocity = normalVelocity + (attributes.velocity - normalVelocity) * (1.0 - friction);
    }
    float3 offset = attributes.position - position;
    float3 radial = offset - normal * dot(offset, normal);
    float radius = length(radial);
    float sector = frac(atan2(dot(radial, bitangent), dot(radial, tangent)) / 6.2831853 + 1.0) * 8.0;
    uint ia = (uint)floor(sector) % 8;
    uint ib = (ia + 1) % 8;
    float limitA = ia < 4 ? limitsA[ia % 4] : limitsB[ia % 4];
    float limitB = ib < 4 ? limitsA[ib % 4] : limitsB[ib % 4];
    float limit = max(0.0, min(limitA, limitB));
    if (radius > limit && radius > 0.0001)
    {
        attributes.position -= radial * (1.0 - limit / radius);
        attributes.velocity *= 0.15;
        attributes.age += dt * 4.0;
    }
}
";
    const string SurfaceOutputCode = @"
void SurfaceFade(inout VFXAttributes attributes, in float scale, in float fade)
{
    float progress = saturate(attributes.age / max(attributes.lifetime, 0.0001));
    attributes.alpha *= pow(saturate(1.0 - progress), max(0.1, fade));
    attributes.size *= scale * (1.0 + progress * 0.35);
}
";
}
