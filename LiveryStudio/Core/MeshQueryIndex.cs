using System.Numerics;

namespace LiveryStudio.Core;

// Immutable geometry acceleration for one loaded car. Candidate IDs stay in
// original mesh order so shared UVs and equal-distance hits retain their rules.
internal sealed class MeshQueryIndex
{
    private readonly record struct Bounds(Vector3 Minimum,Vector3 Maximum);
    private readonly record struct Node(Vector3 Minimum,Vector3 Maximum,int Left,int Right,int Start,int Count);
    private readonly Node[] worldNodes,uvNodes;
    private readonly int[] worldOrder,uvOrder;
    public MeshQueryIndex(SurfaceVertex[] vertices)
    {
        var count=vertices.Length/3;var world=new Bounds[count];var uv=new Bounds[count];var paint=new List<int>();
        worldOrder=Enumerable.Range(0,count).ToArray();
        for(var triangle=0;triangle<count;triangle++)
        {
            var a=vertices[triangle*3];var b=vertices[triangle*3+1];var c=vertices[triangle*3+2];
            world[triangle]=new(Vector3.Min(a.Position,Vector3.Min(b.Position,c.Position)),Vector3.Max(a.Position,Vector3.Max(b.Position,c.Position)));
            uv[triangle]=new(new(Vector2.Min(a.UV,Vector2.Min(b.UV,c.UV)),0),new(Vector2.Max(a.UV,Vector2.Max(b.UV,c.UV)),0));
            if(a.Kind==1)paint.Add(triangle);
        }
        uvOrder=paint.ToArray();worldNodes=Build(worldOrder,world);uvNodes=Build(uvOrder,uv);
    }
    private static Node[] Build(int[] order,Bounds[] bounds)
    {
        if(order.Length==0)return [];
        var nodes=new List<Node>();
        static float Axis(Vector3 value,int axis)=>axis==0?value.X:axis==1?value.Y:value.Z;
        var comparers=Enumerable.Range(0,3).Select(axis=>Comparer<int>.Create((a,b)=>
        {
            var first=Axis(bounds[a].Minimum+bounds[a].Maximum,axis);var second=Axis(bounds[b].Minimum+bounds[b].Maximum,axis);
            var comparison=first.CompareTo(second);return comparison!=0?comparison:a.CompareTo(b);
        })).ToArray();
        int Split(int start,int count)
        {
            var minimum=new Vector3(float.MaxValue);var maximum=new Vector3(float.MinValue);
            for(var i=start;i<start+count;i++){minimum=Vector3.Min(minimum,bounds[order[i]].Minimum);maximum=Vector3.Max(maximum,bounds[order[i]].Maximum);}
            var extent=maximum-minimum;var padding=extent*.000021f+new Vector3(.0000001f);var index=nodes.Count;nodes.Add(default);
            if(count<=8){nodes[index]=new(minimum-padding,maximum+padding,-1,-1,start,count);return index;}
            var axis=extent.X>=extent.Y&&extent.X>=extent.Z?0:extent.Y>=extent.Z?1:2;
            Array.Sort(order,start,count,comparers[axis]);var half=count/2;var left=Split(start,half);var right=Split(start+half,count-half);
            nodes[index]=new(minimum-padding,maximum+padding,left,right,0,0);return index;
        }
        Split(0,order.Length);return nodes.ToArray();
    }
    public List<int> Ray(Vector3 origin,Vector3 direction)
    {
        var result=new List<int>();if(worldNodes.Length==0)return result;Span<int> stack=stackalloc int[64];var pending=0;stack[pending++]=0;
        while(pending>0)
        {
            var node=worldNodes[stack[--pending]];if(!Intersects(origin,direction,node.Minimum,node.Maximum))continue;
            if(node.Count>0){for(var i=node.Start;i<node.Start+node.Count;i++)result.Add(worldOrder[i]);}
            else{stack[pending++]=node.Left;stack[pending++]=node.Right;}
        }
        result.Sort();return result;
    }
    private static bool Intersects(Vector3 origin,Vector3 direction,Vector3 minimum,Vector3 maximum)
    {
        var near=0f;var far=float.PositiveInfinity;
        for(var axis=0;axis<3;axis++)
        {
            var o=axis==0?origin.X:axis==1?origin.Y:origin.Z;var d=axis==0?direction.X:axis==1?direction.Y:direction.Z;
            var low=axis==0?minimum.X:axis==1?minimum.Y:minimum.Z;var high=axis==0?maximum.X:axis==1?maximum.Y:maximum.Z;
            if(d==0){if(o<low||o>high)return false;continue;}
            var first=(low-o)/d;var second=(high-o)/d;near=MathF.Max(near,MathF.Min(first,second));far=MathF.Min(far,MathF.Max(first,second));if(near>far)return false;
        }
        return true;
    }
    public List<int> Uv(Vector2 point)=>Uv(point,point);
    public List<int> Uv(Vector2 minimum,Vector2 maximum)
    {
        var result=new List<int>();if(uvNodes.Length==0)return result;Span<int> stack=stackalloc int[64];var pending=0;stack[pending++]=0;
        while(pending>0)
        {
            var node=uvNodes[stack[--pending]];if(maximum.X<node.Minimum.X||minimum.X>node.Maximum.X||maximum.Y<node.Minimum.Y||minimum.Y>node.Maximum.Y)continue;
            if(node.Count>0){for(var i=node.Start;i<node.Start+node.Count;i++)result.Add(uvOrder[i]);}
            else{stack[pending++]=node.Left;stack[pending++]=node.Right;}
        }
        result.Sort();return result;
    }
}
