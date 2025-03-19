//#define LODCROSSROAD

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LODRoad{
[ExecuteInEditMode][DisallowMultipleComponent][RequireComponent(typeof(LODGroup))]
public class Spline : MonoBehaviour {
    #if UNITY_EDITOR || LODROAD_INCLUDE_IN_BUILD
    public static Material lastMat;

    [MenuItem("GameObject/3D Object/LODRoad")]
    public static void NewRoad(MenuCommand menuCommand){
        GameObject g = new GameObject("Road",new Type[]{typeof(Spline)});
        GameObjectUtility.EnsureUniqueNameForSibling(g);
        GameObjectUtility.SetParentAndAlign(g, menuCommand.context as GameObject);
        Undo.RegisterCreatedObjectUndo(g, "Create Road " + g.name);
        Selection.activeObject = g;
    }

    [System.Serializable]
    public class Node
    {
        public static float smoothness=1.0f/3.0f;
        public Transform t=null;
        private Vector3 tOldPos;
        private Quaternion tOldRot;
        private bool hasT=false;
        public Vector3 pos,k0,k1;
        public float kdir;
        public bool auto=true, flipLinkDir=false;
        public Node(Vector3 pos){this.pos=pos;this.kdir=0.5f;k0=k1=Vector3.zero;}
        public Node(Node src){
            t=src.t;
            tOldPos=src.tOldPos;
            tOldRot=src.tOldRot;
            pos=src.pos;
            k0=src.k0;
            k1=src.k1;
            kdir=src.kdir;
            auto=src.auto;
            flipLinkDir=src.flipLinkDir;
        }
        public Node(){}

        public Vector3 PosAbs(){
            return pos+((t==null)?Vector3.zero:t.position);
        }
        public Vector3 K0Abs(){
            Vector3 a=pos+k0;
            if(t!=null){a += t.position;}
            return a;
        }
        public Vector3 K1Abs(){
            Vector3 a=pos+k1;
            if(t!=null){a += t.position;}
            return a;
        }
        public void UpdateTangents(Vector3 prev,Vector3 next){
            Vector3 pos=PosAbs();
            //if(t!=null){pos=t.position;}
            if(auto){
                k1 = Vector3.Lerp(pos-prev, next - pos, kdir) * Node.smoothness;
                k0 =-k1;
            }
        }
        public bool UpdatePosFromT(){
            if(t==null){
                if(hasT){
                    pos+=tOldPos;
                    tOldPos=Vector3.zero;
                    tOldRot=Quaternion.identity;
                    hasT=false;
                    return true;
                }
                return false;
            }
            if(t.position!=tOldPos || t.rotation!=tOldRot){
                tOldPos=t.position;tOldRot=t.rotation;return true;
            }
            return false;
        }
        public bool SetLink(Transform link,bool stick=false){
            if(link==t){return false;}
            Vector3 rel =(t==null) ? Vector3.zero : t.position;
            Vector3 rel2=(link==null) ? Vector3.zero : link.position;
            pos=(stick) ? Vector3.zero : pos+rel-rel2;
            t=link;
            hasT=t!=null;
            return true;
        }
    }
    public List<Node> nodes =new List<Node>();

    [System.Serializable]
    public class Road : ICloneable{
        public AnimationCurve widthProfileL = AnimationCurve.Constant(0, 1, 1);
        public AnimationCurve widthProfileR = AnimationCurve.Constant(0, 1, 1);
        public Vector2Int wProfLAlign=Vector2Int.one*-1, wProfRAlign=Vector2Int.one*-1;
        public float widthMarginTerrain = 1.0f, heightMarginTerrain = 0.01f, smoothTerrain=0.5f;
        public Vector4 uv=new Vector4(0,0,1,1), uvCurb=new Vector4(0,0,1,1);
        public Vector2 widthScaleOffset = new Vector2(1,0);
        public Vector2 range=new Vector2(0,1);
        public enum UVMode:byte{Relative=0,LeftAbs=1,RightAbs=2};
        public UVMode UVWidth= UVMode.Relative;
        public enum ZigZagMode:byte{Left=0,Right=1,None=2};
        public ZigZagMode zigZagMode= ZigZagMode.None;
        public bool editHeightmap = true;
        public int colliderLod =-1;
        public float extrusion=0.1f,curbWidth=0.05f;
        public bool extrude=false, hasCurb=false, genMesh=true;

        public object Clone(){
            Road newRoad=new Road();
            newRoad.widthScaleOffset=widthScaleOffset;
            newRoad.widthProfileL=AnimationCurve.Constant(0,1,widthProfileL.Evaluate(1));
            newRoad.widthProfileR=AnimationCurve.Constant(0,1,widthProfileR.Evaluate(1));
            newRoad.widthMarginTerrain=widthMarginTerrain;
            newRoad.uv=uv;
            newRoad.uvCurb=uvCurb;
            newRoad.UVWidth=UVWidth;
            newRoad.editHeightmap=editHeightmap;
            newRoad.colliderLod=colliderLod;
            newRoad.extrusion=extrusion;
            newRoad.curbWidth=curbWidth;
            newRoad.extrude=extrude;
            newRoad.hasCurb=hasCurb;
            newRoad.genMesh=genMesh;
            return newRoad;
        }

        public Mesh GenMesh(Spline spline, SplineLOD detail, bool isForTerrain,string name){
            List<Node> nodes =spline.nodes;
            if(nodes.Count<=1){return null;}
            UpdateNodes(nodes);
            List<Vector3> verts=new List<Vector3>();
            List<Vector2> uvs=new List<Vector2>();
            List<Vector3> nrms=new List<Vector3>();
            List<int> idxs=new List<int>();

            AnimationCurve lCurve=widthProfileL, rCurve=widthProfileR;
            Vector2 lScale=widthScaleOffset, rScale=widthScaleOffset;
            GetCurve(spline.roads,wProfLAlign, ref lCurve, ref lScale);
            GetCurve(spline.roads,wProfRAlign, ref rCurve, ref rScale);
            float wlcomp=(lScale.x/widthScaleOffset.x);//*((wProfLAlign.y&1)==0 ? -1 : 1);
            float wrcomp=(rScale.x/widthScaleOffset.x);//*((wProfRAlign.y&1)==0 ? -1 : 1);

            float u=0.0f,len= GetBezierLength(nodes, detail);
            float a=0, olda=0,dist=range.x*len;
            float wl=widthProfileL.Evaluate(dist/len)+(lCurve==widthProfileL ? 0 : (1-lCurve.Evaluate(dist/len))*wlcomp),oldwl=wl;
            float wr=widthProfileR.Evaluate(dist/len)-(rCurve==widthProfileR ? 0 : (1-rCurve.Evaluate(dist/len))*wrcomp),oldwr=wr;
            Vector3 oldPos= nodes[0].PosAbs(),tg,tg2,p1;
            int k=0;
            oldPos=p1 = GetBezierPoint(nodes,nodes[0].PosAbs(),0,range.x*len,ref k,ref a, out tg);
            a+=detail.step+k;
            float safeRange=(range.y>=1)?float.MaxValue:range.y*len;

            if(extrude&&!isForTerrain) {
                InsertRectCap(verts,uvs,nrms,idxs,p1-spline.transform.position,tg,u,wl,wr,isForTerrain,true);
            }
            AdvanceRoad(verts,uvs,nrms,idxs,p1-spline.transform.position,tg,u,wl,wr,isForTerrain);

            while(k< nodes.Count && dist<safeRange){
                Vector3 p2 = p1;
                k=Mathf.FloorToInt(a);
                p1= GetBezierPoint(nodes,k,a%1,out tg);
                dist+=Vector3.Distance(p2,p1);
                wl=widthProfileL.Evaluate(dist/len)+(lCurve==widthProfileL ? 0 : (1-lCurve.Evaluate(dist/len))*wlcomp);
                wr=widthProfileR.Evaluate(dist/len)-(rCurve==widthProfileR ? 0 : (1-rCurve.Evaluate(dist/len))*wrcomp);
                float areaw=((p1-oldPos)*Math.Max(Math.Abs(oldwl-wl),Math.Abs(oldwr-wr))).magnitude;

                float mida=(a+olda)*0.5f;
                Vector3 mid=GetBezierPoint(nodes,Mathf.FloorToInt(mida),mida%1,out tg2);
                float area=Vector3.Cross(oldPos-mid,p1-mid).magnitude;

                if(Mathf.Max(area,areaw)>detail.minArea||k==nodes.Count-1){
                    u+=Vector3.Distance(oldPos,p1);
                    //of1=((oldwl-wl)*widthScaleOffset.x) * ( ((wl+wr)*widthScaleOffset.x) / (dist-olddist) );
                    AdvanceRoad(verts,uvs,nrms,idxs,p1-spline.transform.position,tg,u,wl,wr,isForTerrain);
                    oldPos=p1;
                    olda=a;
                    oldwl=wl; oldwr=wr;
                }
                a+=detail.step;
            }
            if(extrude&&!isForTerrain) {
                idxs.RemoveRange(idxs.Count-6*3,6*3);
                InsertRectCap(verts,uvs,nrms,idxs,p1-spline.transform.position,tg,u,wl,wr,isForTerrain,false);
            }
            else{
                idxs.RemoveRange(idxs.Count-2*3,2*3);
            }
            Mesh m=new Mesh();
            m.vertices=verts.ToArray();
            m.triangles=idxs.ToArray();
            m.uv=uvs.ToArray();
            m.normals=nrms.ToArray();
            m.name=name;
            return m;
        }

        void AdvanceRoad(List<Vector3> poss,List<Vector2> uvs,List<Vector3> nrms,List<int> idxs,Vector3 pos,Vector3 fwd,float dist, float wl, float wr, bool isForTerrain){//isForTerrain=true;
            Vector3 right=Vector3.Cross(Vector3.up,fwd).normalized;
            Vector3 pl,pr;
            GetVertPos(pos,fwd,wl,wr,isForTerrain,0,out pl,out pr);
            float uvw=(UVWidth==UVMode.Relative)? widthScaleOffset.x:(pl-pr).magnitude;
            bool[] sharp=null;
            Vector3[] line;
            Vector2[] uvy;
            float[] to;
            float uvd=dist*uv[2]+uv[0], uvd2=dist*uvCurb[2]+uvCurb[0];
            if(extrude&&!isForTerrain){
                Vector3 pc=Vector3.up*extrusion;
                if(hasCurb){
                    sharp=new bool[]{true,true,true,true};
                    line=new Vector3[]{pl, pl+pc, pl+pc+right*curbWidth, pr+pc-right*curbWidth, pr+pc, pr};
                    Vector2 e=new Vector2(0,extrusion),c=new Vector2(0,curbWidth);
                    uvy=new Vector2[]{e-e, e, e,e+c, new Vector2(0,.5f), new Vector2(0,.5f+uvw), e+c, e+c+c,e+c+c, e+e+c+c};
                    to=new float[]{0,0,0,0.5f,0.5f,0.5f};
                    float nrmw=1/(widthScaleOffset.x+extrusion*2+curbWidth*2);
                    int i=0;
                    for(;i<4;i++){uvy[i].Set(uvd2,uvy[i].y*nrmw*uvCurb[3]+uvCurb[1]);}
                    for(;i<6;i++){uvy[i].Set(uvd,uvy[i].y*nrmw*uv[3]+uv[1]);}
                    for(;i<uvy.Length;i++){uvy[i].Set(uvd2,uvy[i].y*nrmw*uvCurb[3]+uvCurb[1]);}
                }
                else{
                    sharp=new bool[]{true,true};
                    line=new Vector3[]{pl, pl+pc, pr+pc, pr};
                    Vector2 e=new Vector2(0,extrusion),w=new Vector2(0,uvw);
                    uvy=new Vector2[]{e-e, e, e, e+w, e+w, e+e+w};
                    to=new float[]{0,0,0.5f,0.5f};
                    float nrmw=1/(widthScaleOffset.x+extrusion*2);
                    for(int i=0;i<uvy.Length;i++){uvy[i].Set(uvd,uvy[i].y*nrmw*uv[3]+uv[1]);}
                }
            }
            else{
                line=new Vector3[]{pl, pr};
                Vector2 uvx=(isForTerrain)?new Vector2(0,1):new Vector2(uv[1],(uvw/widthScaleOffset.x)*uv[3]+uv[1]);
                uvy=new Vector2[]{new Vector2(uvd,uvx[0]), new Vector2(uvd,uvx[1])};
                to=new float[]{0,0.5f};
                //nrmw=1/(widthScaleOffset.x);
            }
            if(UVWidth==UVMode.RightAbs){Array.Reverse(uvy);}
            if(zigZagMode==ZigZagMode.Left){Array.Reverse(to);for(int i=0;i<to.Length;i++){to[i]=-to[i];}}
            else if(zigZagMode==ZigZagMode.None){for(int i=0;i<to.Length;i++){to[i]=0;}}

            int at=poss.Count, atu=0, nverts=uvy.Length, startto=nverts*2+(extrude?4:0);
            for (int i = 0; i < line.Length; i++) {
                poss.Add(line[i]/*+new Vector3(0,uvy[atu].y,0)*/);
                uvs.Add(uvy[atu++]);
                if(poss.Count>startto){
                    //float mov=lwd*(Vector3.Distance(poss[poss.Count-1],poss[poss.Count-1-nverts]) / Vector3.Distance(poss[poss.Count-nverts],poss[poss.Count-nverts*2]));
                    //mov*=(editHeightmap?0:0.5f);
                    poss[poss.Count-nverts-1]=Vector3.LerpUnclamped(poss[poss.Count-nverts-1],poss[poss.Count-1],to[i]);
                    uvs[uvs.Count-nverts-1]=Vector2.LerpUnclamped(uvs[uvs.Count-nverts-1],uvs[uvs.Count-1],to[i]);
                }
                if(i>0&&i<line.Length-1&&sharp[i-1]){
                    nrms.Add(Vector3.Cross(line[i-1]-line[i],fwd).normalized);

                    poss.Add(line[i]);
                    uvs.Add(uvy[atu++]);
                    nrms.Add(Vector3.Cross(line[i]-line[i+1],fwd).normalized);
                    if(poss.Count>startto){
                        poss[poss.Count-nverts-1]=Vector3.LerpUnclamped(poss[poss.Count-nverts-1],poss[poss.Count-1],to[i]);
                        uvs[uvs.Count-nverts-1]=Vector2.LerpUnclamped(uvs[uvs.Count-nverts-1],uvs[uvs.Count-1],to[i]);
                    }
                }
                else{
                    nrms.Add(Vector3.Cross(line[i==0?0:i-1] - line[i==line.Length-1?line.Length-1:i+1],fwd).normalized);
                }
            }

            for(int i = 0; i < line.Length-1; i++){
                if(i!=0&&sharp[i-1]){at++;}
                idxs.InsertRange(idxs.Count, new int[]{at,at+nverts,at+1, at+1,at+nverts,at+nverts+1});
                at++;
            }
        }

        private void GetVertPos(Vector3 pos,Vector3 fwd,float wl,float wr,bool isForTerrain,float curv,out Vector3 pl,out Vector3 pr){
            Vector3 right=Vector3.Cross(Vector3.up,fwd).normalized;
            if(isForTerrain){pos-=Vector3.up*heightMarginTerrain;}
            float wm = (isForTerrain ? widthMarginTerrain : 0.0f);
            wl=widthScaleOffset.y - wl*0.5f*widthScaleOffset.x - wm;
            wr=widthScaleOffset.y + wr*0.5f*widthScaleOffset.x + wm;
            pl=pos + right*(wl);
            pr=pos + right*(wr);
        }

        private void InsertRectCap(List<Vector3> poss,List<Vector2> uvs,List<Vector3> nrms,List<int> idxs,Vector3 pos,Vector3 fwd,float dist, float wl,float wr,bool isForTerrain,bool start){
            Vector3 pl,pr;
            GetVertPos(pos,fwd,wl,wr,isForTerrain,0,out pl,out pr);
            Vector3 pc=Vector3.up*extrusion;
            float nrmw;
            float sign = start?-1:1;
            fwd*=sign;
            poss.Add(pl);    poss.Add(pl + pc); poss.Add(pr + pc); poss.Add(pr);
            nrms.Add(fwd);   nrms.Add(fwd);     nrms.Add(fwd);     nrms.Add(fwd);
            if(extrude&&!isForTerrain){nrmw=1/(widthScaleOffset.x + ((hasCurb&&!isForTerrain) ? curbWidth*2 : 0) + extrusion*2);}
            else{nrmw=1/(widthScaleOffset.x);}
            uvs.Add((Vector2)uv);
            uvs.Add(new Vector2(0,extrusion)*nrmw*new Vector2(uv[2],uv[3])+(Vector2)uv);
            uvs.Add(new Vector2((pl-pr).magnitude,extrusion)*nrmw*new Vector2(uv[2],uv[3])+(Vector2)uv);
            uvs.Add(new Vector2((pl-pr).magnitude,0)*nrmw*new Vector2(uv[2],uv[3])+(Vector2)uv);
            int at=poss.Count-4;
            if(start){idxs.InsertRange(idxs.Count,new int[]{at,at+1,at+2,at,at+2,at+3});}
            else{idxs.InsertRange(idxs.Count,new int[]{at,at+3,at+2,at,at+2,at+1});}
        }

        public GameObject GenGameObject(Transform t,int LODIndex,Mesh mesh,string name){
            GameObject roadObj=t.Find(name)?.gameObject;
            if(roadObj==null){
                roadObj=new GameObject(name,new Type[]{typeof(MeshFilter), typeof(MeshRenderer)});
                roadObj.transform.parent=t;
            }
            roadObj.transform.position=t.position;
            roadObj.transform.rotation=Quaternion.identity;
            MeshFilter mf=roadObj.GetComponent<MeshFilter>();
            MeshRenderer mr=roadObj.GetComponent<MeshRenderer>();
            if(genMesh){
                if(mf==null){mf=roadObj.AddComponent<MeshFilter>();}
                mf.mesh=mesh;

                if(mr==null){mr=roadObj.AddComponent<MeshRenderer>();}
                if(mr.sharedMaterial==null){
                    string chname=roadObj.name.Substring(0,roadObj.name.Length-1);
                    for (int i = 0; i < t.childCount; i++) {
                        Transform tch=t.GetChild(i);
                        MeshRenderer _mr=null;
                        if(tch.gameObject.name.Contains(chname)){_mr=tch.GetComponent<MeshRenderer>();}
                        if(_mr==null){continue;}
                        if(_mr.sharedMaterial!=null){mr.material=_mr.sharedMaterial;break;}
                    }
                }
                if(mr.sharedMaterial==null){mr.material = lastMat;}

            }
            else{
                PreferredDestroyMethod(mf);
                PreferredDestroyMethod(mr);
            }
            MeshCollider col=roadObj.GetComponent<MeshCollider>();
            if(colliderLod==-1){PreferredDestroyMethod(col);}
            else if(LODIndex==colliderLod){
                if(col==null){col=roadObj.AddComponent<MeshCollider>();}
                col.sharedMesh=mesh;
            }
            return roadObj;
        }
        public static void GetCurve(List<Road> roads, Vector2Int sel, ref AnimationCurve curve, ref Vector2 scaleOffset){
            if(sel.x>=0&&sel.x<roads.Count) {
                Road r=roads[sel.x];
                if (sel.y == 1) {
                    curve = r.widthProfileR;
                    scaleOffset = r.widthScaleOffset;
                }
                else if (sel.y == 0) {
                    curve = r.widthProfileL;
                    scaleOffset = r.widthScaleOffset; scaleOffset.x *= -1;
                }
            }
        }
        //public static float GetExtent(AnimationCurve curve,)
    }
    public List<Road> roads = new List<Road>(){new Road()};

    [System.Serializable]
    public class SplineLOD:ICloneable {
        public float step=0.05f,minArea=0.02f;
        public object Clone(){
            SplineLOD newLOD = new SplineLOD(){step=this.step, minArea=this.minArea};
            return newLOD;
        }
    }
    public const int maxDetailLevels=3;
    public int NDetailLevels = 1;
    public SplineLOD[] detailLevels =new SplineLOD[maxDetailLevels]{new Spline.SplineLOD(), new Spline.SplineLOD(), new Spline.SplineLOD()};

    [System.Serializable]
    public class SpawnedObj:ICloneable{
        public GameObject src;
        public List<GameObject> spawned=new List<GameObject>();
        public float distance=0,density=3;
        public int orientation;
        public Vector2Int relativeTo=Vector2Int.one*-1;
        public Vector2 range=new Vector2(0,1);
        public object Clone(){
            SpawnedObj newSp=new SpawnedObj();
            newSp.density=density;
            newSp.distance=distance;
            newSp.src=src;
            newSp.relativeTo=relativeTo;
            newSp.orientation=orientation;
            return newSp;
        }
        public void SpawnLine(Spline trg, GameObject newSrc,string name){
            if(src!=newSrc){
                foreach (GameObject g in spawned){PreferredDestroyMethod(g);}
                spawned.Clear();
                src=newSrc;
            }
            if(src==null){return;}

            Transform group = trg.transform.Find(name);
            if(group==null){group = new GameObject(name).transform;}
            group.parent=trg.transform;

            Vector2 scaleOffset=new Vector2(1,0);
            AnimationCurve curve=null;
            Road.GetCurve(trg.roads,relativeTo,ref curve,ref scaleOffset);

            float a = 0, dist = 0.0f, len = GetBezierLength(trg.nodes, trg.detailLevels[0]);
            Vector3 tg=(trg.nodes[0].K1Abs()-trg.nodes[0].PosAbs()).normalized, p1=trg.nodes[0].PosAbs();
            int k=0,nSpawns=0;
            p1=GetBezierPoint(trg.nodes,trg.nodes[0].PosAbs(),0,range.x*len,ref k,ref a, out tg);
            dist=range.x*len;

            while(k<trg.nodes.Count&&dist<range.y*len){
                float w = distance;
                if (curve != null) {w += (curve.Evaluate(dist / len) * 0.5f * scaleOffset.x + scaleOffset.y);}
                Vector3 spawnPos = p1 + Vector3.Cross(Vector3.up,tg) * w;
                if (nSpawns >= spawned.Count) {spawned.Add(null);}
                if (spawned[nSpawns] == null) {spawned[nSpawns] = GameObject.Instantiate(src);}
                Transform t=spawned[nSpawns].transform;
                t.parent = group;
                t.position = spawnPos;
                t.rotation = Quaternion.identity;
                Vector3 tg2=tg;
                if(orientation>1){tg2.y=0;}
                if(orientation>0){t.LookAt(t.position+tg2);}
                spawned[nSpawns].name = "Spawn_" + src.name + "_" + nSpawns;
                float olddist=dist;
                dist+=density;
                nSpawns++;

                p1=GetBezierPoint(trg.nodes,p1,olddist,dist,ref k,ref a, out tg);
            }

            for (int i = nSpawns; i < spawned.Count; i++) {PreferredDestroyMethod(spawned[i]);}
            spawned.RemoveRange(nSpawns,spawned.Count - nSpawns);
        }
    }
    public List<SpawnedObj> spawnedObjs =new List<SpawnedObj>();

    [System.Serializable]
    public class ExtrudedObj:ICloneable{
        public GameObject[] src=new GameObject[maxDetailLevels];
        public GameObject[] startCap=new GameObject[maxDetailLevels];
        public GameObject[] endCap=new GameObject[maxDetailLevels];
        public Vector2 range=new Vector2(0,1);
        public float distance=0;
        public Vector2Int relativeTo=Vector2Int.one*-1;
        public int colliderLod=-1;
        public object Clone(){
            ExtrudedObj newObj=new ExtrudedObj();
            for (int i = 0; i < maxDetailLevels; i++) {
                newObj.src[i]=src[i];
                newObj.startCap[i]=startCap[i];
                newObj.endCap[i]=endCap[i];
            }
            newObj.distance=distance;
            newObj.relativeTo=relativeTo;
            newObj.colliderLod=colliderLod;
            return newObj;
        }
        private class Vec3Sort:IComparer<Vector3>{
            public int Compare(Vector3 a,Vector3 b){
                //return Mathf.CeilToInt(a.z-b.z);
                float e=1e-03f,diff=a.z-b.z;
                if(diff<e&&diff>-e){return 0;}
                return diff>0?1:-1;
            }
        }
        public Mesh GenMesh(Spline trg,int LODIndex,string name){
            if(src[LODIndex]==null){return null;}   MeshFilter mf=src[LODIndex].GetComponent<MeshFilter>();
            if(mf==null){return null;}              Mesh srcMesh=mf.sharedMesh;
            if(srcMesh==null){return null;}
            Mesh mesh=new Mesh();

            int nVerts=srcMesh.vertexCount;
            Vector2[] srcUVs=srcMesh.uv;
            Vector3[] srcNrms=srcMesh.normals;
            Vector3[] poss=new Vector3[nVerts];
            int[] possPerm = new int[nVerts];
            {
                Vector3[] poss2 = srcMesh.vertices;
                for (int i = 0; i < nVerts; i++) {
                    possPerm[i] = i;
                    poss2[i] = src[LODIndex].transform.localToWorldMatrix.MultiplyVector(poss2[i]);
                    srcNrms[i] = src[LODIndex].transform.localToWorldMatrix.MultiplyVector(srcNrms[i]);
                }
                Array.Sort(poss2, possPerm, new Vec3Sort());
                for (int i = 0; i < nVerts; i++) {poss[possPerm[i]] = poss2[i];}
            }
            Vector3 tg=Vector3.forward,firstTg=tg, p1=trg.nodes[0].PosAbs(),firstP1=p1;
            int k=0,offset=0,l=0;
            float a=0,oldVert=poss[0].z,newVert, len = GetBezierLength(trg.nodes, trg.detailLevels[0]),dist=range.x*len,w=0;
            Vector3[] outNrms=new Vector3[poss.Length];
            Vector3[] outPoss=new Vector3[poss.Length];
            Vector2[] outUVs=new Vector2[poss.Length];

            Vector2 scaleOffset=new Vector2(1,0);
            AnimationCurve curve=null;
            Road.GetCurve(trg.roads,relativeTo,ref curve,ref scaleOffset);

            p1=firstP1=GetBezierPoint(trg.nodes,trg.nodes[0].PosAbs(),0,dist,ref k,ref a, out firstTg);
            while(k<trg.nodes.Count && dist<range.y*len && outPoss.Length<100000){
                oldVert=poss[possPerm[0]].z;
                Array.Resize(ref outPoss,outPoss.Length+nVerts);
                Array.Resize(ref outUVs,outUVs.Length+nVerts);
                Array.Resize(ref outNrms,outNrms.Length+nVerts);
                for(int j = 0; j < poss.Length; j++){
                    int i=possPerm[j];
                    newVert=poss[i].z;
                    float olddist=dist;
                    dist+=newVert-oldVert;
                    //p1=GetBezierPoint(trg.nodes,p1,newVert-oldVert,ref k,ref a, out tg);
                    p1=GetBezierPoint(trg.nodes,p1,olddist,dist,ref k,ref a, out tg);
                    Vector3 right=Vector3.Cross(Vector3.up,tg).normalized;
                    Vector3 up=Vector3.Cross(tg,right).normalized;

                    w = (curve == null) ? distance : (curve.Evaluate(dist / len) * 0.5f * scaleOffset.x + scaleOffset.y + distance);
                    outPoss[i+offset]=(right*(poss[i].x+w) + up*poss[i].y + p1 - trg.transform.position);
                    outNrms[i+offset]=(right*srcNrms[i].x + up*srcNrms[i].y + tg*srcNrms[i].z);
                    outUVs[i+offset]=(srcUVs[i]);
                    oldVert=newVert;
                }

                offset+=nVerts;
                l++;
            }
            mesh.vertices=outPoss;
            mesh.normals=outNrms;
            mesh.uv=outUVs;

            for (int i = 0; i < srcMesh.subMeshCount; i++) {
                int[] indices=srcMesh.GetIndices(i);
                int[] outIdxs=new int[indices.Length*l];
                for (int j = 0; j < outIdxs.Length; j++) {outIdxs[j]=indices[j%indices.Length]+(j/indices.Length)*nVerts;}
                mesh.SetIndices(outIdxs,srcMesh.GetTopology(i),i,true);
            }

            Mesh startMesh,endMesh;
            GetMeshInGameObject(startCap[LODIndex],out startMesh);
            GetMeshInGameObject(endCap[LODIndex],out endMesh);
            if(startMesh!=null||endMesh!=null){
                Mesh c=new Mesh();

                CombineInstance[] ci=new CombineInstance[(startMesh!=null&&endMesh!=null) ? 3:2];
                int i=0;
                ci[i].mesh = mesh; ci[i++].transform = Matrix4x4.identity;
                if(startMesh!=null){
                    Vector3 ps=firstP1;
                    Vector3 fwd=firstTg, right=Vector3.Cross(Vector3.up,fwd).normalized, up=Vector3.Cross(fwd,right);
                    //Vector3 ps=trg.nodes[0].PosAbs();
                    //Vector3 fwd=(trg.nodes[0].K1Abs()-ps).normalized, right=Vector3.Cross(Vector3.up,fwd).normalized, up=Vector3.Cross(fwd,right);
                    float ws = (curve == null) ? distance : (curve.Evaluate(0) * 0.5f * scaleOffset.x + scaleOffset.y + distance);

                    Matrix4x4 startm=Matrix4x4.identity;
                    startm.SetColumn(0,right);
                    startm.SetColumn(1,up);
                    startm.SetColumn(2,fwd);
                    startm.SetColumn(3,ps-trg.transform.position+right*ws);
                    startm=startm*startCap[LODIndex].transform.localToWorldMatrix;

                    ci[i].mesh = startMesh; ci[i++].transform = startm;
                }
                if(endMesh!=null){
                    Vector3 ps=p1;
                    Vector3 fwd=-tg, right=Vector3.Cross(Vector3.up,fwd).normalized, up=Vector3.Cross(fwd,right);
                    //Vector3 ps=trg.nodes[trg.nodes.Count-1].PosAbs();
                    //Vector3 fwd=(trg.nodes[trg.nodes.Count-1].K0Abs()-ps).normalized, right=Vector3.Cross(Vector3.up,fwd).normalized, up=Vector3.Cross(fwd,right);

                    Matrix4x4 endm=Matrix4x4.identity;
                    endm.SetColumn(0,right);
                    endm.SetColumn(1,up);
                    endm.SetColumn(2,fwd);
                    endm.SetColumn(3,ps-trg.transform.position-right*w);
                    endm=endm*startCap[LODIndex].transform.localToWorldMatrix;

                    ci[i].mesh = endMesh; ci[i++].transform = endm;
                }


                c.CombineMeshes(ci,true,true,false);
                mesh=c;
            }

            mesh.name=name;
            return mesh;
        }
        void GetMeshInGameObject(GameObject obj,out Mesh mesh){
            mesh=null;
            MeshFilter mf=null;
            //MeshRenderer mr=null;
            if(obj!=null){mf=obj.GetComponent<MeshFilter>();}
            if(mf!=null){mesh=mf.sharedMesh;}
            //if(mr!=null){mats=mr.sharedMaterials;}
        }

        public GameObject GenGameObject(Transform t,int LODIndex,Mesh m,string name){
            GameObject exObj=t.Find(name)?.gameObject;
            if(exObj==null){
                exObj=new GameObject(name,new Type[]{typeof(MeshFilter), typeof(MeshRenderer)});
                exObj.transform.parent=t;
            }
            exObj.transform.position=t.position;
            exObj.transform.rotation=Quaternion.identity;
            exObj.GetComponent<MeshFilter>().mesh=m;
            //roadObj.GetComponent<MeshRenderer>().material=(material==null)?lastMat:material;

            MeshCollider col=exObj.GetComponent<MeshCollider>();

            if(colliderLod==-1){PreferredDestroyMethod(col);}
            else if(LODIndex==colliderLod){
                if(col==null){col=exObj.AddComponent<MeshCollider>();}
                col.sharedMesh=m;
            }
            return exObj;
        }

    }
    public List<ExtrudedObj> extrudedObjs=new List<ExtrudedObj>();

    /*[System.Serializable]
    public class Decal{
        Rect pos=new Rect(0,0,1,1);
        Material mat;
    }
    public List<Decal> decals=new List<Decal>();*/

    public void SetListCount<T> (List<T>list,ObjName nameFunc,int count)where T:new(){
        while(count> list.Count){
            list.Add(new T());}
        while(count< list.Count){
            DeleteLODGameObjects(nameFunc,list.Count-1);
            list.RemoveAt(list.Count-1);
        }
    }
    public delegate string ObjName(int LODIndex,int listIndex);
    public static string RoadName(int LODIndex,int listIndex){return "Road"+listIndex+"_LOD"+LODIndex;}
    public static string ExtrudedName(int LODIndex,int listIndex){return "Extruded"+listIndex+"_LOD"+LODIndex;}
    public static string SpawnName(int LODIndex,int listIndex){return "Spawned"+listIndex;}
    public void DeleteLODGameObjects(ObjName nameFunc,int listIndex){
        for(int i = 0; i < NDetailLevels; i++){
            Transform t=transform.Find(nameFunc(i,listIndex));
            if(t!=null){PreferredDestroyMethod(t.gameObject);}
        }
    }

    public void GenRoad(bool genRoads=true,bool genLines=true,bool genExtruded=true){
        if(genRoads){genLines = genExtruded = genRoads;}
        if(lastMat==null){lastMat=AssetDatabase.GetBuiltinExtraResource<Material>("Default-Diffuse.mat");}
        LODGroup lodg = GetComponent<LODGroup>();
        LOD[] ls=lodg.GetLODs();
        bool resetTransition = ls.Length < NDetailLevels;
        Array.Resize<LOD>(ref ls,NDetailLevels);

        for(int j=0;j<NDetailLevels;j++){
            int k=0;
            ls[j].renderers=new Renderer[roads.Count+extrudedObjs.Count];
            for (int i = 0; i < roads.Count && genRoads; i++){
                Mesh m = roads[i].GenMesh(this, detailLevels[j], false, gameObject.name + RoadName(j, i));
                ls[j].renderers[k++] = roads[i].GenGameObject(transform, j, m, RoadName(j, i)).GetComponent<MeshRenderer>();
            }

            for (int i = 0; i < extrudedObjs.Count && genExtruded; i++){
                Mesh exm=extrudedObjs[i].GenMesh(this,j,gameObject.name+ExtrudedName(j,i));
                ls[j].renderers[k++]=extrudedObjs[i].GenGameObject(transform,j,exm,ExtrudedName(j,i)).GetComponent<MeshRenderer>();
            }

        }
        for(int j=NDetailLevels;j<maxDetailLevels;j++){
            for (int i = 0; i < roads.Count; i++) {
                Transform t=transform.Find(RoadName(j,i));
                if(t!=null){PreferredDestroyMethod(t.gameObject);}
            }
            for (int i = 0; i < extrudedObjs.Count; i++) {
                Transform t=transform.Find(ExtrudedName(j,i));
                if(t!=null){PreferredDestroyMethod(t.gameObject);}
            }
        }

        if(resetTransition){for(int j=1;j<ls.Length;j++){ls[j].screenRelativeTransitionHeight = 0.5f-0.1f*j;}}
        lodg.SetLODs(ls);
        EditorUtility.SetDirty(lodg);

        for(int j=0;j<spawnedObjs.Count && genLines;j++){spawnedObjs[j].SpawnLine(this, spawnedObjs[j].src,SpawnName(0,j));}

        EditorUtility.SetDirty(this);
    }

    public static void UpdateNodes(List<Node> n){
        if(n.Count<2){return;}
        float sign = n[0].flipLinkDir?-1:1;
        Vector3 delta=n[0].PosAbs()-n[1].PosAbs();
        Vector3 prev=n[0].PosAbs() + ((n[0].t==null) ? delta : -n[0].t.forward*sign * delta.magnitude);
        n[0].UpdateTangents(prev,n[1].PosAbs());

        for(int i = 1; i < n.Count - 1; i++){n[i].UpdateTangents(n[i-1].PosAbs(),n[i+1].PosAbs());}

        sign = n[n.Count-1].flipLinkDir?-1:1;
        delta=n[n.Count-1].PosAbs()-n[n.Count-2].PosAbs();
        Vector3 next=n[n.Count-1].PosAbs() + ((n[n.Count - 1].t==null) ? delta : n[n.Count - 1].t.forward*sign * delta.magnitude);
        n[n.Count - 1].UpdateTangents(n[n.Count - 2].PosAbs(),next);
    }
    public bool Connect(Node node, Transform to) {
        int idx=nodes.IndexOf(node);
        if((idx==0||idx==nodes.Count-1) && to!=null){
            node.SetLink(to, true);
            node.flipLinkDir = idx != 0;
            node.kdir = idx != 0?1:0;
            return true;
        }
        return false;
    }

#if UNITY_EDITOR
    private void UndoCb(){
        if(this!=null){GenRoad();}
    }
    public void OnDisable(){
        TerrainDelta.UnPaint();
        AssetDatabase.SaveAssets();
    }
    void Start(){
        if(TerrainDelta.lastCommand!=TerrainDelta.LastCommand.Paint){TerrainDelta.Paint();}
        Undo.undoRedoPerformed += UndoCb;
    }
    void Update(){
        bool regen=false;
        foreach (Node n in nodes){regen = n.UpdatePosFromT() || regen;}
        if(regen){GenRoad();}
    }
    public static void PreferredDestroyMethod(UnityEngine.Object o){DestroyImmediate(o);}
#else
    public static void PreferredDestroyMethod(UnityEngine.Object o){Destroy(o);}
#endif

#if UNITY_2020_3_OR_NEWER && !UNITY_2021_1 && !UNITY_2021_2 && !UNITY_2022_1
        public static T[] PreferredGetAllObjects<T>() where T : UnityEngine.Object { return Component.FindObjectsByType<T>(FindObjectsSortMode.None); }
#else
        public static T[] PreferredGetAllObjects<T>() where T : UnityEngine.Object { return Component.FindObjectsOfType<T>(); }
#endif    

    public static void CopyParams(Spline src,Spline trg){
        trg.roads.Clear();
        foreach (Road r in src.roads){trg.roads.Add((Road)r.Clone());}
        trg.NDetailLevels=src.NDetailLevels;
        for (int i = 0; i < src.detailLevels.Length; i++) {trg.detailLevels[i]=(SplineLOD)src.detailLevels[i].Clone();}
        trg.spawnedObjs.Clear();
        foreach (SpawnedObj sp in src.spawnedObjs){trg.spawnedObjs.Add((SpawnedObj)sp.Clone());}
        trg.extrudedObjs.Clear();
        foreach (ExtrudedObj ex in src.extrudedObjs){trg.extrudedObjs.Add((ExtrudedObj)ex.Clone());}
    }
    public static float GetBezierLength(List<Node> nodes,SplineLOD lod){
        if(nodes ==null){return 0.0f;}
        if(nodes.Count<2){return 0.0f;}
        float len=0.0f, a=lod.step;
        Vector3 oldPos= nodes[0].PosAbs();
        int k=0;

        while(k< nodes.Count){
            Vector3 dump, p1= GetBezierPoint(nodes,k,a,out dump);
            len+=Vector3.Distance(oldPos,p1);
            oldPos=p1;

            a+=lod.step;
            if(a>1.0f){a-=1.0f;k++;}
        }
        return len;
    }

    public static Vector3 GetBezierPoint(List<Node> nodes,int idx,float a,out Vector3 tangent){
        Node n0=nodes[Mathf.Min(idx,nodes.Count-1)];
        Node n1=nodes[Mathf.Min(idx+1,nodes.Count-1)];
        if(n0==n1){a=1.0f;}//end of nodes reached

        Vector3 p1=Vector3.Lerp(n0.PosAbs(),n0.K1Abs(), a);
        Vector3 p2=Vector3.Lerp(n0.K1Abs(), n1.K0Abs(), a);
        Vector3 p3=Vector3.Lerp(n1.K0Abs(), n1.PosAbs(),a);
        p1=Vector3.Lerp(p1,p2,a);
        p2=Vector3.Lerp(p2,p3,a);
        tangent=(p2-p1).normalized;
        p1=Vector3.Lerp(p1,p2,a);
        return p1;
    }
    public static Vector3 GetBezierPoint(List<Node> nodes,Vector3 lastPos,float currDist,float reqDist,ref int k,ref float a,out Vector3 tg){
        float e=1e-03f, advanceStep=Mathf.Max(Mathf.Min(0.1f,reqDist-currDist),e);
        GetBezierPoint(nodes,k,a,out tg);
        if(advanceStep<=e){return lastPos;}
        while(k< nodes.Count && currDist<reqDist) {
            float aTmp=-1;int kTmp=-1;
            float d=float.MaxValue,step= advanceStep;
            Vector3 newPos= lastPos;
            for(; step > e && (d > advanceStep || currDist+d > reqDist); step*=0.5f){
                aTmp = a+step;
                kTmp=k+((aTmp > 1)?1:0);
                aTmp-=(aTmp>1 ? 1 : 0);

                newPos = GetBezierPoint(nodes, kTmp , aTmp, out tg);
                d = Vector3.Distance(newPos, lastPos);
            }
            a+=step;
            currDist+=d;
            lastPos =newPos;
            if(a>1.0f){a-=1.0f;k++;}
        }
        return lastPos;
    }

    public static bool CleanupLink(Transform t,bool force=false){
        if(t==null){return false;}
        if(t.GetComponentInParent<Spline>()==null){return false;}
        Spline[] splines=PreferredGetAllObjects<Spline>();
        if(force){
            foreach (Spline s in splines){
                foreach (Node n in s.nodes){if(n.t==t){n.SetLink(null);}}
            }
            PreferredDestroyMethod(t.gameObject); return true;
        }
        else{
            bool found=false;
            foreach (Spline s in splines){
                foreach (Node n in s.nodes){ if(n.t==t){found=true;goto end;}}
            }
            end:
            if(!found){PreferredDestroyMethod(t.gameObject); return true;}
        }
        return false;
    }

    public void Append(Spline other, bool otherStart, bool trgStart){
        if(otherStart == trgStart){other.nodes.Reverse();}
        Node otherLast=other.nodes[other.nodes.Count-1], thisLast= nodes[nodes.Count-1];
        if(trgStart){
            if(otherLast.t== nodes[0].t){
                otherLast.SetLink(null);
                nodes[0].SetLink(null);
            }
            other.nodes.RemoveAt(other.nodes.Count-1);
            nodes.InsertRange(0, other.nodes);
        }
        else{
            if (other.nodes[0].t == thisLast.t) {
                Transform clear=thisLast.t;
                other.nodes[0].SetLink(null);
                thisLast.SetLink(null);
                Spline.CleanupLink(clear);
            }
            other.nodes.RemoveAt(0);
            nodes.InsertRange(nodes.Count, other.nodes);
        }

        for (int i = 0; i < other.transform.childCount; i++) {
            Transform t= other.transform.GetChild(i);
            t.SetParent(transform,true);
            PreferredDestroyMethod(t.gameObject.GetComponent<MeshFilter>());
            PreferredDestroyMethod(t.gameObject.GetComponent<MeshRenderer>());
            Spline.CleanupLink(t);
        }
        PreferredDestroyMethod(other.gameObject);
    }
#endif
}


[CustomEditor(typeof(Spline))]
public class SplineEditor : Editor
{
    private Color firstColor=Color.green, lastColor = Color.red, defaultColor;
    private Color firstColorG=new Color(0,0.5f,0), lastColorG=new Color(0.5f,0,0), defaultColorG=Color.gray;
    private bool canAdd=false;
    private Spline.Node selectedNodeTrg = null, selectedNode2 =null;

    void OnEnable(){
        if(this.target==null){return;}
        defaultColor=Handles.color;
    }

    private void nodeHandlePosSizeColor(Spline s,Spline.Node n,bool sIsTrg,out Vector3 pos,out float size,out Color col){
        pos=n.PosAbs();
        bool paddEnds = s.nodes.Count>1&&!sIsTrg;
        if(n==s.nodes[0]){
            if(paddEnds){pos=Vector3.Lerp(pos,s.nodes[1].PosAbs(),0.1f);}
            col=sIsTrg ? firstColor : firstColorG;
        }
        else if(n==s.nodes[s.nodes.Count-1]){
            if(paddEnds){pos=Vector3.Lerp(pos,s.nodes[s.nodes.Count-2].PosAbs(),0.1f);}
            col=sIsTrg ? lastColor : lastColorG;
        }
        else{col=sIsTrg ? defaultColor : defaultColorG;}
        size=HandleUtility.GetHandleSize(pos);
    }

    private bool BasicGUI(Spline trg, bool selectOther){
        if(Event.current.type==EventType.MouseDrag){Undo.RecordObject(trg, "Move LODRoad node");}
        bool regen=false;
        Color colorBkp=Handles.color;
        for(int i = 0;i<trg.nodes.Count-1;i++){
            Vector3 k0a=trg.nodes[i+1].K0Abs();
            Vector3 k1a=trg.nodes[i].K1Abs();
            Handles.DrawBezier(trg.nodes[i].PosAbs(),trg.nodes[i+1].PosAbs(),k1a,k0a,Color.white,null,1.0f);
        }
        for(int i = 0;i<trg.nodes.Count;i++){
            Spline.Node edited = trg.nodes[i];
            Vector3 k0a=edited.K0Abs(), k1a=edited.K1Abs(), pa=edited.PosAbs();
            Vector3 rel=(edited.t==null) ? Vector3.zero : edited.t.position;
            Quaternion q=(edited.t==null)?Quaternion.identity:edited.t.rotation;

            Handles.color=new Color(1,1,1,0.5f);
            Handles.DrawLine(k0a, pa);
            Handles.DrawLine(k1a, pa);
            Handles.color=colorBkp;

            EditorGUI.BeginChangeCheck();
            edited.pos=Handles.PositionHandle(pa,q)-rel;
            regen=EditorGUI.EndChangeCheck()||regen;

            if(edited.t!=null){
                Handles.color=new Color(1,1,1,0.5f); Handles.DrawDottedLine(edited.t.position,pa,3.0f);
                Handles.color=Color.blue;  Handles.DrawLine(edited.t.position, edited.t.position+edited.t.forward);
                Handles.color=Color.green; Handles.DrawLine(edited.t.position, edited.t.position+edited.t.up);
                Handles.color=Color.red;   Handles.DrawLine(edited.t.position, edited.t.position+edited.t.right);
            }

        }

        Spline[] splines=Spline.PreferredGetAllObjects<Spline>();
        Handles.color=new Color(.5f,.75f,1,1);
        foreach (Spline s in splines){
            if(s==trg){continue;}
            foreach(Spline.Node n in s.nodes){
                Vector3 handlePos = n.PosAbs();
                float buttonSize=HandleUtility.GetHandleSize(handlePos)* 0.1f;
                if(Handles.Button(handlePos,Quaternion.identity,buttonSize,buttonSize,Handles.SphereHandleCap)) {
                    Selection.objects=new UnityEngine.Object[]{s.gameObject};
                }
            }
        }
        Handles.color=colorBkp;

        return regen;
    }
    private GameObject Link(Spline.Node selTrg, Spline.Node selNode2,Transform parent, Vector3 fwd){
        GameObject link = null;
        if (selNode2.t == null) {
            link = new GameObject();
            link.transform.position = selNode2.PosAbs() + Vector3.up;
            link.transform.parent = parent;
        }
        else {link = selNode2.t.gameObject;}
        link.transform.LookAt(link.transform.position+fwd,Vector3.up);

        Transform a = selTrg.t, b=selNode2.t;
        if(selTrg.SetLink(link.transform)){Spline.CleanupLink(a);}
        if(selNode2.SetLink(link.transform)){Spline.CleanupLink(b);}
        return link;
    }

    private bool LinkCtrl(Spline trg,Spline other,ref Spline.Node selTrg,ref Spline.Node selNode2){
        bool regen=false;
        int trgIdx=(trg==null)?-1:trg.nodes.IndexOf(selTrg);
        //int othIdx=(other==null)?-1:other.nodes.IndexOf(selNode2);

        if(selTrg != null && selNode2 !=null){
            Undo.RecordObject(trg, "Link LODRoad nodes");
            Undo.RecordObject(other, "Link LODRoad nodes");
            GameObject link=Link(selTrg,selNode2,trg.gameObject.transform,(selTrg.PosAbs()-selNode2.PosAbs()));
            Undo.RegisterCompleteObjectUndo(link,"Link LODRoad nodes");

            if(other==null){link.name="["+trgIdx+"]_["+trgIdx+"]";}
            else{link.name="["+trgIdx+"]_"+other.gameObject.name+"["+other.nodes.IndexOf(selNode2)+"]";}

            /*if((trgIdx==0||trgIdx==trg.Nodes.Count-1)&&othIdx==0){
                selNode2.flipLinkDir=true;
            }
            if(trgIdx==0&&(othIdx==0||othIdx==other.Nodes.Count-1)){selTrg.flipLinkDir=true;}*/

            Transform a = selTrg.t, b=selNode2.t;
            if(selTrg.SetLink(link.transform)){Spline.CleanupLink(a);}
            if(selNode2.SetLink(link.transform)){Spline.CleanupLink(b);}

            selTrg = selNode2 =null;
            regen = true;
        }

#if LODCROSSROAD
        Hub[] hubs=Spline.PreferredGetAllObjects<Hub>();
        Color colorBkp=Handles.color;
        Handles.color=new Color(.5f,.5f,.5f,1);
        foreach (Hub h in hubs){
            int seli = HubEditor.ClickedConnection(h,null);
            if(seli!=-1){
                if(trg!=null){regen = trg.Connect(selTrg,h.transform.Find(Hub.LinkName(seli))) || regen;}
                else if(other!=null){regen = other.Connect(selNode2,h.transform.Find(Hub.LinkName(seli))) || regen;}
            }
        }
        Handles.color=colorBkp;
#endif

        EditorGUI.BeginChangeCheck();
        for(int i=0;i<trg.nodes.Count;i++){
            Spline.Node edited = trg.nodes[i];
            if(edited.t!=null){
                Vector3 RmLinkPos = (edited.t.position+edited.PosAbs())*0.5f;
                float buttonSize=HandleUtility.GetHandleSize(RmLinkPos) * 0.075f;
                Handles.color=Color.black;
                if(Handles.Button(RmLinkPos, Quaternion.identity, buttonSize, buttonSize, Handles.SphereHandleCap)){
                    Transform t=edited.t;
                    edited.SetLink(null);
                    Spline.CleanupLink(t,false);
                    break;
                }
                RmLinkPos = edited.t.position;
                buttonSize=HandleUtility.GetHandleSize(RmLinkPos) * 0.1f;
                if(Handles.Button(RmLinkPos, Quaternion.identity, buttonSize, buttonSize, Handles.SphereHandleCap)){
                    Spline.CleanupLink(edited.t,true);
                    break;
                }
            }
        }

        if (EditorGUI.EndChangeCheck()) {
            regen = true;
        }
        return regen;
    }
    private bool NodeSelectCtrl(Spline trg, ref Spline.Node selTrg, ref Spline.Node sel2, out Spline sel2spline,bool selectTwo){
        sel2spline=null;
        Spline[] splines = Spline.PreferredGetAllObjects<Spline>();
        bool clicked = false,selTrgFound=false,sel2Found=false;
        foreach (Spline s in splines){
            foreach (Spline.Node n in s.nodes){
                selTrgFound = selTrgFound || (n==selTrg);
                sel2Found = sel2Found || (n==sel2);
            }
            if(selTrgFound && sel2Found){break;}
        }
        if(!selTrgFound){selTrg=null;}
        if(!sel2Found){sel2=null;}

        foreach (Spline s in splines){
            bool sIsTrg = trg==s;
            if(!selectTwo && !sIsTrg){continue;}
            foreach (Spline.Node n in s.nodes){
                Vector3 handlePos;
                float size;
                Color col;
                nodeHandlePosSizeColor(s,n,s==trg,out handlePos,out size,out col);
                size*= (n== selTrg || n== sel2) ? 0.2f : 0.1f;
                Handles.color=col;
                if(Handles.Button(handlePos,Quaternion.identity,size,size,Handles.SphereHandleCap)) {
                    clicked=true;
                    if(!selectTwo){selTrg=(selTrg==n)?null:n;}
                    else if(n == sel2){sel2=null;}
                    else if(n == selTrg){selTrg=null;}
                    else if(sIsTrg && selTrg==null){selTrg = n;}
                    else{sel2=n;}
                }
                Handles.color=defaultColor;
                if(!sIsTrg && n == sel2){sel2spline=s;}
            }
        }
        if(!selectTwo){sel2=null; sel2spline=null;}
        /*for (int i = 0; i < trg.Nodes.Count; i++) {
            if(trg.Nodes[i]==clicked){return i;}
        }*/
        return clicked;
    }
    private bool NodeInsertCtrl(Spline trg, Vector2 mousePos, bool appendOnly){
        Ray r=HandleUtility.GUIPointToWorldRay(mousePos);
        RaycastHit hit;
        if(Physics.Raycast(r,out hit,200.0f)){
            Undo.RecordObject(trg, "Add LODRoad node");
            int minIdx=-1;
            if(appendOnly){
                minIdx=Vector3.Distance(hit.point, trg.nodes[0].PosAbs()) < Vector3.Distance(hit.point, trg.nodes[trg.nodes.Count-1].PosAbs())?
                0:trg.nodes.Count;
            }
            else {
                float minDist = float.MaxValue;
                for(int i = 1; i < trg.nodes.Count; i++) {
                    float dist = HandleUtility.DistancePointLine(hit.point, trg.nodes[i - 1].PosAbs(), trg.nodes[i].PosAbs());
                    if (dist < minDist) {minDist = dist;minIdx = i;}
                }
                if(minIdx==-1){minIdx=0;}
                else if (minIdx == 1 && Vector3.Distance(hit.point, trg.nodes[1].PosAbs()) > Vector3.Distance(trg.nodes[0].PosAbs(), trg.nodes[1].PosAbs())) {
                    minIdx = 0;
                }
                else if(minIdx==trg.nodes.Count-1 &&
                Vector3.Distance(hit.point, trg.nodes[minIdx-1].PosAbs()) > Vector3.Distance(trg.nodes[minIdx-1].PosAbs(),trg.nodes[minIdx].PosAbs())){
                    minIdx = trg.nodes.Count;
                }
            }
            trg.nodes.Insert(minIdx, new Spline.Node(hit.point+Vector3.up*trg.roads[0].heightMarginTerrain));
            return true;
        }
        return false;
    }
    private bool NodeTangentCtrl(Spline trg){
        if(Event.current.type==EventType.MouseDrag){Undo.RecordObject(trg, "Edit LODRoad tangents");}
        EditorGUI.BeginChangeCheck();
        for(int i=0;i<trg.nodes.Count;i++){
            Spline.Node edited = trg.nodes[i];
            Vector3 k0a=edited.K0Abs(), k1a=edited.K1Abs(), pa=edited.PosAbs();
            Quaternion q=(edited.t==null)?Quaternion.identity:edited.t.rotation;

            if(i==0){Handles.color=firstColor;}
            else if(i==trg.nodes.Count-1){Handles.color=lastColor;}
            if(!edited.auto && edited == selectedNodeTrg){
                float handleSize=0.75f;
                Handles.matrix=Matrix4x4.Scale(Vector3.one*handleSize);
                edited.k0=Handles.PositionHandle(k0a/handleSize,q)*handleSize-pa;
                edited.k1=Handles.PositionHandle(k1a/handleSize,q)*handleSize-pa;
                Handles.matrix=Matrix4x4.identity;
            }
        }

        return EditorGUI.EndChangeCheck();
    }
    private bool SegmentCtrl(Spline trg,Spline other,Spline.Node selTrg, Spline.Node selOther){
        int trgIdx=(trg==null)? -1 : trg.nodes.IndexOf(selTrg);
        int othIdx=(other==null)? -1 : other.nodes.IndexOf(selOther);
        bool selTrgEnd = (trgIdx!=-1) && (trgIdx==0||trgIdx==trg.nodes.Count-1);
        bool selOthEnd = (othIdx!=-1) && (othIdx==0||othIdx==other.nodes.Count-1);
        if(trgIdx==-1){return false;}
        else if(othIdx==-1){
            if(!selTrgEnd){
                Undo.RecordObject(trg, "Split LODRoad");
                GameObject g=new GameObject(trg.name,new Type[]{typeof(Spline)});
                Undo.RegisterCreatedObjectUndo(g,"Split LODRoad");
                g.transform.parent=trg.transform.parent;
                GameObjectUtility.EnsureUniqueNameForSibling(g);
                //g.transform.position=trg.gameObject.transform.position;
                Spline newS=g.GetComponent<Spline>();
                newS.nodes =trg.nodes.GetRange(trgIdx,trg.nodes.Count-trgIdx);
                newS.nodes[0]=new Spline.Node(newS.nodes[0]);
                newS.nodes[0].kdir=0;
                //newS.Nodes[0].flipLinkDir=false;
                selTrg.kdir = 1;
                //selTrg.flipLinkDir = false;
                trg.nodes.RemoveRange(trgIdx+1,trg.nodes.Count-1-trgIdx);
                Vector3 fwd;
                Spline.GetBezierPoint(trg.nodes,trg.nodes.Count,1,out fwd);
                GameObject link=Link(selTrg,newS.nodes[0],trg.gameObject.transform,fwd);
                link.name="["+trgIdx+"]_"+g.name+"[0]";
                Spline.CopyParams(trg,newS);
                newS.GenRoad();
                MeshRenderer[] matTrg=g.GetComponentsInChildren<MeshRenderer>();
                MeshRenderer[] matSrc=trg.gameObject.GetComponentsInChildren<MeshRenderer>();
                foreach (MeshRenderer mt in matTrg){
                    foreach (MeshRenderer ms in matSrc){
                        if(String.Equals(mt.gameObject.name,ms.gameObject.name)){mt.sharedMaterial=ms.sharedMaterial;}
                    }
                }

                return true;
            }
            return false;
        }
        else if(selTrgEnd && selOthEnd){
            trg.Append(other, selOther ==other.nodes[0], selTrg == trg.nodes[0]);
            return true;
        }
        return false;
    }

    public void OnSceneGUI(){
        if(this.target==null){return;}
        Spline trg = (Spline)this.target, otherTrg=null;
        Event e = Event.current;
        bool regen=false;

        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        regen = BasicGUI(trg, mode!=EditMode.Link) || regen;

        bool clicked = NodeSelectCtrl(trg,ref selectedNodeTrg,ref selectedNode2,out otherTrg, mode!=EditMode.NodeTangent);

        if(mode==EditMode.NodeTangent){
            if(e.type==EventType.MouseDown){canAdd=true;}
            if(NodeTangentCtrl(trg)){
                regen = true;
                canAdd=false;
            }
            if(clicked){
                int selectedIdxTrg=trg.nodes.IndexOf(selectedNodeTrg);
                if(selectedIdxTrg>-1){
                    if(e.shift){
                        Undo.RecordObject(trg, "Remove LODRoad node");
                        trg.nodes.RemoveAt(selectedIdxTrg);
                        canAdd=false;
                        regen=true;
                    }
                    else{nodeFolds[selectedIdxTrg]=true;Repaint();}
                }
                canAdd=false;
            }
            if(e.type==EventType.MouseUp && e.button==0 && canAdd) {
                regen = NodeInsertCtrl(trg,e.mousePosition,e.shift) || regen;
                canAdd=false;
            }
        }
        else if(mode==EditMode.Link) {
            regen = LinkCtrl(trg,otherTrg,ref selectedNodeTrg, ref selectedNode2) || regen;
        }
        else if(mode==EditMode.Segment) {
            if(SegmentCtrl(trg,otherTrg, selectedNodeTrg,selectedNode2)){
                selectedNode2=null;selectedNodeTrg=null;
                regen=true;
            }
        }
        /////////////
        if(regen){regen=false;trg.GenRoad();}
    }

    private List<bool> nodeFolds =new List<bool>(), spawnLineFolds=new List<bool>(), extrudeLineFolds=new List<bool>(), roadFolds=new List<bool>();
    private bool nodeFold =true, lodFold =false, roadFold=true, spawnLineFold=true, extrudeLineFold=true;
    private enum EditMode{Segment =0,Link =1,NodeTangent =2,Decal=3}
    private EditMode mode=EditMode.NodeTangent;

    public override void OnInspectorGUI() {
        serializedObject.Update();
        if (this.target == null){return;}
        Spline trg = (Spline)this.target;
        bool genRoads=false,genLines=false,genExtruded=false;
        mode=(EditMode)GUILayout.Toolbar((int)mode, new string[]{EditMode.Segment.ToString(),EditMode.Link.ToString(),EditMode.NodeTangent.ToString(),EditMode.Decal.ToString()});

        TerrainDelta.InspectorGUI(trg);

        EditorGUILayout.LabelField("Road length is "+Spline.GetBezierLength(trg.nodes, trg.detailLevels[0])+" m");

        Undo.RecordObject(trg, "Edit LODRoad");

        if (roadFold=EditorGUILayout.Foldout(roadFold, "Roads")) {
            int count = SetListCount(roadFolds,trg.roads.Count,5);
            trg.SetListCount<Spline.Road>(trg.roads,Spline.RoadName, count);
            EditorGUI.indentLevel++;

            for (int i = 0; i < trg.roads.Count; i++){
                Spline.Road road=trg.roads[i];
                GUILayout.BeginHorizontal();
                roadFolds[i]            = EditorGUILayout.Foldout(roadFolds[i], "Road " + i);
                if(ListDeleteAt(trg,trg.roads,Spline.RoadName,i)){break;}
                GUILayout.EndHorizontal();
                if (!roadFolds[i]) {continue;}

                AnimationCurve[] curves;
                string[] cNames;
                GetCurves(trg, out curves,out cNames);
                cNames[0]="Use own curve";

                EditorGUI.BeginChangeCheck();
                road.widthProfileL      = EditorGUILayout.CurveField("Left width curve", road.widthProfileL, Color.green, new Rect(){x = 0,y = 0,width = 1,height = 2});
                road.widthProfileR      = EditorGUILayout.CurveField("Right width curve", road.widthProfileR, Color.green, new Rect(){x = 0,y = 0,width = 1,height = 2});
                road.wProfLAlign        = SelectCurve("Left curve source",road.wProfLAlign,cNames,curves,trg.roads);
                road.wProfRAlign        = SelectCurve("Right curve source",road.wProfRAlign,cNames,curves,trg.roads);
                GUILayout.Space(EditorGUIUtility.singleLineHeight+EditorGUIUtility.standardVerticalSpacing);
                road.widthScaleOffset   = EditorGUILayout.Vector2Field("Width scale & offset", road.widthScaleOffset);
                road.widthScaleOffset.x = Mathf.Clamp(road.widthScaleOffset.x,0.01f,20);
                road.widthScaleOffset.y = Mathf.Clamp(road.widthScaleOffset.y,-15,15);
                road.range              = RangeFieldExact("Start/end offset",road.range,0,1);
                if((road.extrude        = EditorGUILayout.Toggle("Extrude",road.extrude))){
                    road.extrusion      = EditorGUILayout.FloatField("Extrusion",road.extrusion);
                    road.extrusion      = Mathf.Clamp(road.extrusion,-15,15);
                    if((road.hasCurb    = EditorGUILayout.Toggle("Generate curb",road.hasCurb))){
                        road.curbWidth  = EditorGUILayout.FloatField("Curb width",road.curbWidth);
                    }
                }
                road.zigZagMode         = (Spline.Road.ZigZagMode)EditorGUILayout.EnumPopup("Zig-zag mode",road.zigZagMode);

                EditorGUILayout.Separator();
                road.UVWidth            = (Spline.Road.UVMode)
                                          EditorGUILayout.Popup("UV width mode", (int)road.UVWidth, new string[]{"Relative to road width","Absolute - left justified","Absolute - right justified"});
                road.uv                 = MultiFloatField4("UV",road.uv,new GUIContent[]{new GUIContent("X"),new GUIContent("Y"),new GUIContent("W"),new GUIContent("H")});
                if(road.extrude && road.hasCurb){
                    road.uvCurb         = MultiFloatField4("Curb UV",road.uvCurb,new GUIContent[]{new GUIContent("X"),new GUIContent("Y"),new GUIContent("W"),new GUIContent("H")});
                }
                for (int j = 0; j < trg.NDetailLevels; j++) {
                    Transform t         = trg.transform.Find(Spline.RoadName(j,i));
                    if(t==null){continue;}
                    MeshRenderer r      = t.GetComponent<MeshRenderer>();
                    if(r==null){continue;}
                    Material mat        = (Material)EditorGUILayout.ObjectField("LOD"+j+"Material", (UnityEngine.Object)r.sharedMaterial, typeof(Material), true);
                    if(mat!=r.sharedMaterial){Spline.lastMat=r.material=mat;}
                }

                EditorGUILayout.Separator();
                road.editHeightmap      = EditorGUILayout.Toggle("Edit heightmap", road.editHeightmap);
                road.widthMarginTerrain = EditorGUILayout.Slider("Width margin on terrain",road.widthMarginTerrain,0.1f,25.0f);
                road.heightMarginTerrain= EditorGUILayout.Slider("Height margin on terrain",road.heightMarginTerrain,0.0f,1.0f);
                road.smoothTerrain      = EditorGUILayout.Slider("Terrain smoothness",road.smoothTerrain,0.0001f,1.0f);

                EditorGUILayout.Separator();
                road.colliderLod        = EditorGUILayout.Toggle("Generate collider", road.colliderLod!=-1) ? Mathf.Clamp(road.colliderLod,0,trg.NDetailLevels-1) : -1;
                if(road.colliderLod!=-1){
                    road.colliderLod    = EditorGUILayout.IntField("Collider LOD",road.colliderLod);
                    road.colliderLod    = Mathf.Clamp(road.colliderLod,0,trg.NDetailLevels-1);
                }
                road.genMesh            = EditorGUILayout.Toggle("Generate mesh", road.genMesh);
                genRoads                = EditorGUI.EndChangeCheck() || genRoads;
            }
            EditorGUI.indentLevel--;

        }

        EditorGUILayout.Separator();
        GUILayout.BeginHorizontal();
        lodFold = EditorGUILayout.Foldout(lodFold, "Spline LODs");
        GUILayout.EndHorizontal();
        if (lodFold) {
            EditorGUI.BeginChangeCheck();
            trg.NDetailLevels = Mathf.Clamp(EditorGUILayout.IntField("Detail levels", trg.NDetailLevels), 1, 3);
            for (int i = 0; i < trg.NDetailLevels; i++) {
                Spline.SplineLOD lod = trg.detailLevels[i];
                EditorGUILayout.LabelField("LOD " + i);
                EditorGUI.indentLevel++;
                lod.minArea   = EditorGUILayout.Slider("Max error area", lod.minArea, 0.001f, 1.0f);
                lod.step      = EditorGUILayout.Slider("Step", lod.step, 0.005f, 1.0f);
                EditorGUI.indentLevel--;
            }
            genRoads          = EditorGUI.EndChangeCheck() || genRoads;
        }

        EditorGUILayout.Separator();
        nodeFold = EditorGUILayout.Foldout(nodeFold, "Nodes");
        if (nodeFold) {
            while (nodeFolds.Count < trg.nodes.Count) {nodeFolds.Add(false);}
            while (nodeFolds.Count > trg.nodes.Count) {nodeFolds.RemoveAt(nodeFolds.Count - 1);}
            EditorGUI.indentLevel++;
            for (int i = 0; i < trg.nodes.Count; i++) {
                nodeFolds[i]   = EditorGUILayout.Foldout(nodeFolds[i], "Node " + i);
                if (!nodeFolds[i]) {continue;}
                Spline.Node edited = trg.nodes[i];

                EditorGUI.BeginChangeCheck();
                Transform tt   = (Transform)EditorGUILayout.ObjectField("Anchor", (UnityEngine.Object)edited.t, typeof(Transform), true);
                if(tt==edited.t){}
                else if(trg.Connect(edited,tt)){}
                else{edited.SetLink(tt);}

                edited.pos     = (edited.t==null)?edited.pos:edited.t.worldToLocalMatrix.MultiplyVector(edited.pos);
                edited.pos     = EditorGUILayout.Vector3Field((edited.t==null) ? "Pos" : "Pos offset", edited.pos);
                edited.pos     = (edited.t==null)?edited.pos:edited.t.localToWorldMatrix.MultiplyVector(edited.pos);
                GUI.enabled    = !edited.auto;
                edited.k0      = EditorGUILayout.Vector3Field("K0", edited.k0);
                edited.k1      = EditorGUILayout.Vector3Field("K1", edited.k1);
                GUI.enabled    = true;

                if ((i > 0 && i < trg.nodes.Count-1)||edited.t!=null){
                    edited.kdir= TangentSlider(edited.kdir, trg.nodes, i);
                }
                edited.auto    = EditorGUILayout.Toggle("Auto", edited.auto);
                genRoads       = EditorGUI.EndChangeCheck() || genRoads;
            }
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Separator();
        if (spawnLineFold = EditorGUILayout.Foldout(spawnLineFold, "Spawn lines")){
            int count     = SetListCount(spawnLineFolds,trg.spawnedObjs.Count,10);
            trg.SetListCount<Spline.SpawnedObj>(trg.spawnedObjs,Spline.SpawnName,count);

            AnimationCurve[] curves;
            string[] cNames;
            GetCurves(trg, out curves,out cNames);

            EditorGUI.indentLevel++;
            for (int i = 0; i < trg.spawnedObjs.Count; i++) {
                Spline.SpawnedObj sp= trg.spawnedObjs[i];
                GUILayout.BeginHorizontal();
                spawnLineFolds[i]   = EditorGUILayout.Foldout(spawnLineFolds[i], sp.src==null? "Line "+i : sp.src.name);
                if(ListDeleteAt(trg,trg.spawnedObjs,Spline.SpawnName,i)){break;}
                GUILayout.EndHorizontal();
                if (!spawnLineFolds[i]){continue;}

                EditorGUI.BeginChangeCheck();
                GameObject newSrc   = (GameObject)EditorGUILayout.ObjectField("Prefab", (UnityEngine.Object)sp.src, typeof(GameObject), false);
                sp.range            = RangeFieldExact("Start/end offset",sp.range,0,1);
                sp.distance         = EditorGUILayout.FloatField("Side distance", sp.distance);
                sp.density          = EditorGUILayout.FloatField("Density",       sp.density);
                sp.relativeTo       = SelectCurve("Relative to",sp.relativeTo,cNames,curves,trg.roads);
                sp.orientation      = EditorGUILayout.Popup("Orientation", sp.orientation, new string[]{"Global", "Tangent", "Tangent and vertical"});

                if(EditorGUI.EndChangeCheck()){sp.SpawnLine(trg, newSrc,Spline.SpawnName(0,i));}
            }
            EditorGUI.indentLevel--;
        }

        if (extrudeLineFold = EditorGUILayout.Foldout(extrudeLineFold, "Extruded objects")){
            int count       = SetListCount(extrudeLineFolds,trg.extrudedObjs.Count,10);
            trg.SetListCount<Spline.ExtrudedObj>(trg.extrudedObjs,Spline.ExtrudedName, count);

            AnimationCurve[] curves;
            string[] cNames;
            GetCurves(trg, out curves,out cNames);

            EditorGUI.indentLevel++;
            for(int i = 0; i < trg.extrudedObjs.Count; i++) {
                Spline.ExtrudedObj ex   = trg.extrudedObjs[i];
                GUILayout.BeginHorizontal();
                extrudeLineFolds[i]     = EditorGUILayout.Foldout(extrudeLineFolds[i], ex.src[0]==null? "Line "+i : ex.src[0].name);
                if(ListDeleteAt(trg,trg.extrudedObjs,Spline.ExtrudedName,i)){break;}
                GUILayout.EndHorizontal();

                if (extrudeLineFolds[i]){
                    EditorGUI.BeginChangeCheck();
                    ex.range            = RangeFieldExact("Start/end offset",ex.range,0,1);
                    ex.distance         = EditorGUILayout.FloatField("Side distance", ex.distance);
                    ex.relativeTo       = SelectCurve("Relative to",ex.relativeTo,cNames,curves,trg.roads);

                    for(int j = 0; j < Spline.maxDetailLevels && j < trg.NDetailLevels; j++) {
                        EditorGUILayout.PrefixLabel("LOD"+j);
                        float prefixw   = EditorGUIUtility.labelWidth, step=(Screen.width-EditorGUIUtility.labelWidth-30)/3;

                        Rect r=GUILayoutUtility.GetLastRect();   r.x=prefixw; r.width=50;

                        EditorGUI.LabelField(r,"Start");  r.x+=step;   r.width=50;
                        EditorGUI.LabelField(r,"Main");   r.x+=step;   r.width=50;
                        EditorGUI.LabelField(r,"End");

                        GUILayout.Space(EditorGUIUtility.singleLineHeight);
                        r=GUILayoutUtility.GetLastRect();       r.x=prefixw;  r.width=step+10;
                        ex.startCap[j]  = (GameObject)EditorGUI.ObjectField(r,(UnityEngine.Object)ex.startCap[j], typeof(GameObject), false);     r.x+=step;
                        ex.src[j]       = (GameObject)EditorGUI.ObjectField(r,(UnityEngine.Object)ex.src[j],     typeof(GameObject), false);     r.x+=step;
                        ex.endCap[j]    = (GameObject)EditorGUI.ObjectField(r,(UnityEngine.Object)ex.endCap[j], typeof(GameObject), false);
                    }

                    EditorGUILayout.Separator();
                    ex.colliderLod      = EditorGUILayout.Toggle("Generate collider", ex.colliderLod!=-1) ? Mathf.Clamp(ex.colliderLod,0,trg.NDetailLevels-1) : -1;
                    if(ex.colliderLod!=-1){
                        ex.colliderLod  = EditorGUILayout.IntField("Collider LOD",ex.colliderLod);
                        ex.colliderLod  = Mathf.Clamp(ex.colliderLod,0,trg.NDetailLevels-1);
                    }

                    genExtruded         = EditorGUI.EndChangeCheck() || genExtruded;
                }
            }
            EditorGUI.indentLevel--;
        }

        if (genRoads||genLines||genExtruded) {
            trg.GenRoad(genRoads,genLines,genExtruded);
        }
    }

    Vector2 RangeFieldExact(string label, Vector2 range,float min,float max){
        range = EditorGUILayout.Vector2Field(label, range);
        range.x = Mathf.Clamp(range.x, min, max);
        range.y = Mathf.Clamp(range.y, min, max);
        return range;
    }

    Vector2Int SelectCurve(string label, Vector2Int sel,string[] labels,AnimationCurve[] curves,List<Spline.Road> roads, bool checkmark=true){
        int selCurve =checkmark ? (sel.x<0||sel.y<0) ? 0 : sel.x*2+sel.y+1 : -1;
        selCurve = EditorGUILayout.Popup(label, selCurve, labels);
        if(selCurve > 0){sel.x=(selCurve-1)/2; sel.y=(selCurve-1)&1;}
        else if(selCurve == 0){sel=Vector2Int.one*-1; }
        return sel;
    }

    void GetCurves(Spline trg, out AnimationCurve[] curves,out string[] cNames){
        curves= new AnimationCurve[trg.roads.Count*2+1];
        cNames= new string[trg.roads.Count*2+1];
        cNames[0]="Spline";
        for (int i = 0; i < trg.roads.Count*2; i+=2) {
            int ih=i/2, i1=i+1, i2=i+2;
            curves[i1]=(trg.roads[ih].widthProfileL);       curves[i2]=(trg.roads[ih].widthProfileR);
            cNames[i1]=("Road "+ih+" left width curve");    cNames[i2]=("Road "+ih+" right width curve");
        }
    }

    bool ListDeleteAt(Spline trg,IList trgList,Spline.ObjName trgName,int at){
        bool del=GUILayout.Button(new GUIContent("✕"),GUIStyle.none);
        if(del){
            trg.DeleteLODGameObjects(trgName,at);
            trgList.RemoveAt(at);
        }
        return del;
    }

    int SetListCount(List<bool> folds,int currCount,int max){
        int count = Mathf.Min(EditorGUILayout.IntField("Count",currCount),max);
        while (folds.Count < count) {folds.Add(false);}
        while (folds.Count > count) {folds.RemoveAt(folds.Count - 1);}
        return count;
    }

    Vector4 MultiFloatField4(string name,Vector4 val, GUIContent[] labels){
        float[] vals=new float[]{val.x,val.y,val.z,val.w};
        MultiFloatFieldN(vals,new GUIContent(name),labels);
        return new Vector4(vals[0],vals[1],vals[2],vals[3]);
    }
    void MultiFloatFieldN(float[] vals,GUIContent name, GUIContent[] labels){
        Rect r=GUILayoutUtility.GetLastRect();
        r.y+=EditorGUIUtility.singleLineHeight+EditorGUIUtility.standardVerticalSpacing;
        r.width=Screen.width-r.x-21;
        EditorGUI.MultiFloatField(r, new GUIContent(name), labels, vals);
        GUILayout.Space(EditorGUIUtility.singleLineHeight+EditorGUIUtility.standardVerticalSpacing);
    }

    float TangentSlider(float val,List<Spline.Node> nodes,int i){
        bool leftAnchor=(i==0 && nodes[i].t!=null), rightAnchor=(i==nodes.Count-1 && nodes[i].t!=null);
        string link = nodes[i].flipLinkDir ? "Link-" : "Link+";
        string left = leftAnchor ? link : "Previous";
        string right= rightAnchor ? link : "Next";

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("Tangent direction");
        Rect r=GUILayoutUtility.GetLastRect();
        r.x+=r.width;
        r.width=55;
        if(leftAnchor){
            r.x+=5;
            r.width=45;
            if(GUI.Button(r,left)){nodes[i].flipLinkDir=!nodes[i].flipLinkDir;}
            r.x-=5;
        }
        else{GUI.Label(r,left);}

        r.x+=55;
        r.width=Screen.width-r.x-70;
        float ret=GUI.HorizontalSlider(r, val, 0.0f, 1.0f);
        r.x=Screen.width-65;
        r.width=50;
        if(rightAnchor){
            r.width=45;
            if(GUI.Button(r,right)){nodes[i].flipLinkDir=!nodes[i].flipLinkDir;}
        }
        else{GUI.Label(r,right);}
        EditorGUILayout.EndHorizontal();
        return ret;
    }

}
}