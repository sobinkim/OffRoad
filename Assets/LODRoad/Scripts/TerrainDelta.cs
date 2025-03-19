//#define LODCROSSROAD

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace LODRoad{
[System.Serializable]
public class TerrainDelta//:UnityEditor.AssetModificationProcessor
{
#if UNITY_EDITOR || LODROAD_INCLUDE_IN_BUILD
    private static Dictionary<Terrain,RenderTexture> deltasD=new Dictionary<Terrain,RenderTexture>();
    private static bool isInit=false;
    private static Material mat=null;
    //private static string undoPaintName ="LODRoad paint heightmap";
    public enum LastCommand:byte{Paint,Unpaint,None};
    public static LastCommand lastCommand=LastCommand.None;
    private static void Init(){
        if(mat==null){mat = new Material(Shader.Find("Unlit/TerrainDelta"));}
        if(isInit){return;}

        Undo.undoRedoPerformed += ()=>{
            lastCommand=LastCommand.None;
            //Debug.Log("Td undo, ");
            //Clear();
        };
        isInit=true;
    }

    private static void SyncDeltaCache(Terrain[] ts,Material mat){
        Dictionary<Terrain,RenderTexture> newDeltas=new Dictionary<Terrain,RenderTexture>();
        if(deltasD==null){deltasD=newDeltas;}
        foreach(Terrain t in ts){
            RenderTexture heightmap=t.terrainData.heightmapTexture;
            RenderTexture delta=deltasD.ContainsKey(t)?deltasD[t]:new RenderTexture(heightmap.width,heightmap.height,1,RenderTextureFormat.RFloat);
            if(delta.width!=heightmap.width || delta.width!=heightmap.width){
                RenderTexture newDelta=new RenderTexture(heightmap);
                SetUpMaterial(newDelta,mat,0,0,1, 0.5f / t.terrainData.size.y,1,null,delta);
                RenderFullScreenQuadGL();
                delta.Release();
                delta=newDelta;
            }

            newDeltas.Add(t,delta);
        }
        deltasD=newDeltas;
    }

    public static void Clear(){deltasD=null;}

    public static void UnPaint(){
        lastCommand=LastCommand.Unpaint;
        Init();
        if(deltasD==null){return;}
        Terrain[] terrains=Terrain.activeTerrains;
        SyncDeltaCache(terrains,mat);
        //foreach (KeyValuePair<Terrain,RenderTexture> kv in deltasD) {Undo.RecordObject(kv.Key.terrainData, undoPaintName);}

        foreach (KeyValuePair<Terrain,RenderTexture> kv in deltasD){
            TerrainData td = kv.Key.terrainData;
            RenderTexture bkp=RenderTexture.active;
            RenderTexture oldDelta = kv.Value;
            RenderTexture currHeight = td.heightmapTexture;
            RenderTexture newHeight = new RenderTexture(currHeight);
            SetUpMaterial(newHeight, mat, 0, 1, -1,0.5f / td.size.y,0, currHeight, oldDelta);
            RenderFullScreenQuadGL();
            RenderTexture.active=newHeight;
            td.CopyActiveRenderTextureToHeightmap(new RectInt(0,0,newHeight.width,newHeight.height),Vector2Int.zero,TerrainHeightmapSyncControl.HeightAndLod);
            RenderTexture.active=bkp;
            EditorUtility.SetDirty(td);
        }
        Clear();
    }

    public static void Paint(object[] selection=null){
        lastCommand=LastCommand.Paint;
        Init();
        Mesh[] meshes;
        Transform[] transforms;
        float[] smoothness;
        Terrain[] terrains=Terrain.activeTerrains;
        GetSplineAssets(out meshes, out transforms, out smoothness,selection);
        SyncDeltaCache(terrains,mat);
        //foreach (KeyValuePair<Terrain,RenderTexture> kv in deltasD) {Undo.RecordObject(kv.Key.terrainData, undoPaintName);}

        foreach (KeyValuePair<Terrain,RenderTexture> kv in deltasD) {
            TerrainData td = kv.Key.terrainData;
            RenderTexture bkp=RenderTexture.active, oldDelta=kv.Value, newHeight=new RenderTexture(td.heightmapTexture);

            //HERE SUBSTRACT OLD DELTA
            SetUpMaterial(newHeight, mat, 0, 1, -1, 0.5f / td.size.y,0, td.heightmapTexture, oldDelta);
            RenderFullScreenQuadGL();

            //HERE CALC NEW DELTA
            SetUpMaterial(oldDelta, mat, 1, -1, 0, 0.5f / td.size.y,0, newHeight, null);
            Matrix4x4 mx = Matrix4x4.identity;
            mx.SetRow(0, new Vector4(2.0f / td.size.x, 0.0f, 0.0f, -1.0f));
            mx.SetRow(1, new Vector4(0.0f, 0.0f, 2.0f / td.size.z, -1.0f));
            mx.SetRow(2, new Vector4(0.0f, 0.5f / td.size.y, 0.0f, 0.0f));
            GL.Clear(true, true, Color.black, 1.0f);
            GL.PushMatrix();
            GL.modelview = mx;
            GL.LoadProjectionMatrix(Matrix4x4.identity);
            for(int j = meshes.Length - 1; j >= 0; j--){
                SetUpMaterial(oldDelta, mat, 1, -1, 0, 0.5f / td.size.y,1.0f/Mathf.Clamp(smoothness[j],0.0001f,1f), newHeight, null);
                GL.Begin(GL.TRIANGLES);
                if(meshes[j]==null || transforms[j]==null){continue;}
                Vector3 mdc=meshes[j].bounds.center+transforms[j].position, mds=meshes[j].bounds.size;
                mds.y=mdc.y+mds.y*0.5f;
                mdc.y=mdc.y*0.5f;
                if(!new Bounds(mdc, mds).Intersects( new Bounds(td.bounds.center+kv.Key.transform.position,td.bounds.size) )){continue;}

                int[] indices = meshes[j].GetIndices(0);
                Vector3[] verts = meshes[j].vertices;
                Vector2[] uvs=meshes[j].uv;
                Vector3 vo = transforms[j].position - kv.Key.GetPosition();
                for (int i = 0; i < indices.Length; i++){GL.TexCoord2(0,uvs[indices[i]].y);GL.Vertex(verts[indices[i]]+vo);}
                GL.End();
            }
            GL.PopMatrix();

            //HERE ADD NEW DELTA
            SetUpMaterial(td.heightmapTexture, mat, 0, 1, 1, 0.5f / td.size.y,0, newHeight, oldDelta);
            RenderFullScreenQuadGL();

            td.DirtyHeightmapRegion(new RectInt(0, 0, td.heightmapTexture.width, td.heightmapTexture.height), TerrainHeightmapSyncControl.HeightAndLod);
            RenderTexture.active = bkp;
            newHeight.Release();
        }
    }

    public static void SaveRT(RenderTexture rt, string fname){
        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);

        byte[] png=tex.EncodeToPNG();
        File.WriteAllBytes(fname,png);
        Debug.Log("Saved Render Texture "+fname);
    }

    private static void SetUpMaterial(RenderTexture trg,Material mat,float mw,float hw,float dw,float hm,float smoothness,RenderTexture ht,RenderTexture dt){
        if(trg==ht||trg==dt){Debug.LogError("Cyclic rendering");}
        RenderTexture.active=trg;
        mat.SetFloat("_MeshWeight", mw);
        mat.SetFloat("_HeightmapWeight", hw);
        mat.SetFloat("_DeltaWeight", dw);
        mat.SetFloat("_HeightMult", hm);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetTexture("_HeightTex", ht);
        mat.SetTexture("_DeltaTex", dt);
        mat.SetPass(0);
    }
    private static void RenderFullScreenQuadGL(){
        GL.Clear(true, true, Color.black, 1.0f);
        GL.PushMatrix();
        GL.modelview = Matrix4x4.identity;
        GL.LoadProjectionMatrix(Matrix4x4.identity);
        GL.Begin(GL.TRIANGLES);
        GL.Vertex3(-1, -1, 0);  GL.Vertex3(-1, 1, 0);  GL.Vertex3(1, 1, 0);
        GL.Vertex3(1, 1, 0);    GL.Vertex3(1, -1, 0);  GL.Vertex3(-1, -1, 0);
        GL.End();
        GL.PopMatrix();
    }

    private static void GetSplineAssets(out Mesh[] meshes,out Transform[] transforms,out float[] smoothness,object[] selection){
        Spline[] splines=Spline.PreferredGetAllObjects<Spline>();
        List<float> lsmooth = new List<float>();
        List<Mesh> lmeshes = new List<Mesh>();
        List<Transform> ltransforms = new List<Transform>();

        foreach (Spline s in splines){
            if(selection!=null){if(Array.IndexOf(selection,s)==-1){continue;}}
            if(!s.isActiveAndEnabled) {continue;}
            for (int i = 0; i < s.roads.Count; i++) {
                if (!s.roads[i].editHeightmap || !s.isActiveAndEnabled) {continue;}
                lsmooth.Add(s.roads[i].smoothTerrain);
                lmeshes.Add(s.roads[i].GenMesh(s, s.detailLevels[0],true,"TerrainDelta"+lmeshes.Count+"_"+s.name));
                ltransforms.Add(s.transform.Find(Spline.RoadName(0,i)));
            }
        }
#if LODCROSSROAD
        Hub[] hubs=Spline.PreferredGetAllObjects<Hub>();
        foreach (Hub h in hubs){
            if(selection!=null){if(Array.IndexOf(selection,h)==-1){continue;}}
            if(!h.editHeightmap || !h.isActiveAndEnabled) {continue;}
            lsmooth.Add(h.terrainSmooth);
            lmeshes.Add(h.GenMesh(0, h.roundabout,true,true,"TerrainDelta"+lmeshes.Count+"_"+h.name));
            ltransforms.Add(h.transform.Find(Hub.LODName(0)));
        }
#endif
        smoothness=lsmooth.ToArray();
        meshes=lmeshes.ToArray();
        transforms=ltransforms.ToArray();
    }

    private static void ApplyTerrainChngMenu(object o) {
        TerrainDelta.UnPaint();
        foreach(Terrain t in Terrain.activeTerrains){Undo.RecordObject(t.terrainData, "Apply LODRoad terrain changes");}
        TerrainDelta.Paint((object[])o);
        TerrainDelta.Clear();
    }

    public static void InspectorGUI(Component sel){
        //GUIStyle s = EditorGUIUtility.GetBuiltinSkin(EditorSkin.Inspector).FindStyle("DropDownButton");
        GUILayout.BeginHorizontal(new GUILayoutOption[]{GUILayout.ExpandHeight(true),GUILayout.MinHeight(25)});
        if(GUILayout.Button("Paint terrain",EditorStyles.miniButton)){TerrainDelta.Paint();}
        GUI.enabled = deltasD!=null;
        if(GUILayout.Button("Unpaint terrain",EditorStyles.miniButton)){TerrainDelta.UnPaint();}
        GUI.enabled = true;
        if(GUILayout.Button("Apply terrain changes",EditorStyles.miniButtonLeft)){ApplyTerrainChngMenu(null);}
        if(GUILayout.Button("▾",EditorStyles.miniButtonRight,GUILayout.MaxWidth(17))){
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("All current changes"),false, ApplyTerrainChngMenu,null);
            menu.AddItem(new GUIContent("Only this object"),false, ApplyTerrainChngMenu,new object[]{sel});
            menu.ShowAsContext();
        }
        GUILayout.EndHorizontal();
    }
#endif
}
}