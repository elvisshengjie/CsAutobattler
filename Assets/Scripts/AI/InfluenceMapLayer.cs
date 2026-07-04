using System;

[Serializable]
public sealed class InfluenceMapLayer
{
    public string name;
    public float[] values = Array.Empty<float>();

    public InfluenceMapLayer(string layerName, int count)
    {
        name = layerName;
        Resize(count);
    }

    public void Resize(int count)
    {
        if (values == null || values.Length != count)
        {
            values = new float[count];
        }
        else
        {
            Array.Clear(values, 0, values.Length);
        }
    }

    public float this[int index]
    {
        get => index >= 0 && index < values.Length ? values[index] : 0f;
        set { if (index >= 0 && index < values.Length) values[index] = value; }
    }
}
