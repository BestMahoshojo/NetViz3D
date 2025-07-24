using System;
using System.Collections.Generic;

[Serializable]
public class DetailedLayerInfo
{
    public string name;
    public string type;
    public List<int> output_shape;
    public string details;
}

[Serializable]
public class InputImageData
{
    public List<int> pixels;
    public int width;
    public int height;
}

[Serializable]
public class LayerUpdateData
{
    public string layer_name;
    public List<List<List<float>>> activations;
    public float min_val;
    public float val_range;
}

[Serializable]
public class ConvStepData
{
    public string input_layer_name;
    public string output_layer_name;
    public List<int> input_start_coords;
    public int kernel_size;
    public List<int> output_coord;
    public float output_value;
    public float min_val;
    public float val_range;
}

[Serializable]
public class PoolStepData
{
    public string input_layer_name;
    public string output_layer_name;
    public List<int> input_start_coords;
    public int pool_size;
    public List<int> output_coord;
    public float output_value;
    public float min_val;
    public float val_range;
}