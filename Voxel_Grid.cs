﻿//'Hare: Accelerated Multi-Resolution Ray Tracing (GPL)
//'
//'Copyright (c) 2008 - 2025, Open Research in Acoustical Science and Education, Inc. - a 501(c)3 nonprofit			
//'This program is free software; you can redistribute it and/or modify
//'it under the terms of the GNU General Public License as published 
//'by the Free Software Foundation; either version 3 of the License, or
//'(at your option) any later version.
//'This program is distributed in the hope that it will be useful,
//'but WITHOUT ANY WARRANTY; without even the implied warranty of
//'MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//'GNU General Public License for more details.
//'
//'You should have received a copy of the GNU General Public 
//'License along with this program; if not, write to the Free Software
//'Foundation, Inc., 675 Mass Ave, Cambridge, MA 02139, USA.

using System;
using System.Collections.Generic;

namespace Hare
{
    namespace Geometry
    {
        /// <summary>
        /// Concept based on Amanatides Fast Ray-Voxel Traversal Algorithm.
        /// </summary>
        public class Voxel_Grid : Spatial_Partition
        {
            public int[,][] Poly_Ray_ID;
            protected int VoxelCtX, VoxelCtY, VoxelCtZ;
            protected AABB[, ,] Voxels;
            uint no_of_boxes = 500;
            public List<int>[,,,] Voxel_Inv;
            protected Point BoxDims;
            protected Point BoxDims_Inv;
            protected Point VoxelDims;
            protected Point VoxelDims_Inv;
            protected AABB OBox;
            protected double Epsilon = 0.001;
            protected int XYTot;
            uint rayno = 0;
            object ctlock = new object();

            /// <summary>
            /// The voxel grid constructor. Concept based on Amanatides Fast Ray-Voxel Traversal Algorithm.
            /// </summary>
            /// <param name="Model_in"> The array of Topology to be entered into the Voxel Grid. For a single topology, enter an array with a single topology.</param> 
            public Voxel_Grid(Topology[] Model_in, int Domain)
            {
                //Initialize all variables
                Model = Model_in;                
                Point MaxPT = new Point(Double.NegativeInfinity, Double.NegativeInfinity, Double.NegativeInfinity);
                Point MinPT = new Point(Double.PositiveInfinity, Double.PositiveInfinity, Double.PositiveInfinity);
                Poly_Ray_ID = new int[Model.Length, no_of_boxes][]; //bool[Model.Length, System.Environment.ProcessorCount][];

                for (int i = 0; i < Model.Length; i++)
                {
                    for (int p = 0; p < no_of_boxes; p++)
                    {
                        Poly_Ray_ID[i, p] = new int[Model[i].Polygon_Count];
                    }
                }

                //Get the max and min points of each topology...
                for (int m = 0; m < Model.Length; m++)
                {
                    if ((Model[m].Max.x + 0.01) > MaxPT.x) MaxPT.x = (Model[m].Max.x + Epsilon);
                    if ((Model[m].Max.y + 0.01) > MaxPT.y) MaxPT.y = (Model[m].Max.y + Epsilon);
                    if ((Model[m].Max.z + 0.01) > MaxPT.z) MaxPT.z = (Model[m].Max.z + Epsilon);
                    if ((Model[m].Min.x - 0.01) < MinPT.x) MinPT.x = (Model[m].Min.x - Epsilon);
                    if ((Model[m].Min.y - 0.01) < MinPT.y) MinPT.y = (Model[m].Min.y - Epsilon);
                    if ((Model[m].Min.z - 0.01) < MinPT.z) MinPT.z = (Model[m].Min.z - Epsilon);
                }

                OBox = new AABB(MinPT.x - .1, MinPT.y - .1, MinPT.z - .1, MaxPT.x + .1, MaxPT.y + .1, MaxPT.z + .1);

                VoxelCtX = Domain;
                VoxelCtY = Domain;
                VoxelCtZ = Domain;
                XYTot = VoxelCtX * VoxelCtY;

                Voxels = new AABB[VoxelCtX, VoxelCtY, VoxelCtZ];
                Voxel_Inv = new List<int>[VoxelCtX, VoxelCtY, VoxelCtZ, Model.Length];

                BoxDims = new Point(OBox.Max.x - OBox.Min.x, OBox.Max.y - OBox.Min.y, OBox.Max.z - OBox.Min.z);
                VoxelDims = new Point(BoxDims.x / VoxelCtX, BoxDims.y / VoxelCtY, BoxDims.z / VoxelCtZ);
                VoxelDims_Inv = new Point(1 / VoxelDims.x, 1 / VoxelDims.y, 1 / VoxelDims.z);
                BoxDims_Inv = new Point(1 / BoxDims.x, 1 / BoxDims.y, 1 / BoxDims.z);
                
                Char_Step = (VoxelDims.x < VoxelDims.y) ? ((VoxelDims.x < VoxelDims.z) ? VoxelDims.x : VoxelDims.z) : (VoxelDims.y < VoxelDims.z ? VoxelDims.y : VoxelDims.z);
                
                int processorCT = System.Environment.ProcessorCount;

                for (int m = 0; m < Model.Length; m++)
                {
                    System.Threading.Thread[] T_List = new System.Threading.Thread[processorCT];
                    for (int P_I = 0; P_I < processorCT; P_I++)
                    {
                        ThreadParams T = new ThreadParams(P_I * VoxelCtX / processorCT, (P_I + 1) * VoxelCtX / processorCT, P_I, m);
                        System.Threading.ParameterizedThreadStart TS = new System.Threading.ParameterizedThreadStart(delegate { Fill_Voxels(T); });
                        T_List[P_I] = new System.Threading.Thread(TS);
                        T_List[P_I].Start();
                    }

                    bool finished = false;

                    do
                    {
                        System.Threading.Thread.Sleep(100);
                        finished = true;
                        for (int t = 0; t < T_List.Length; t++)
                        {
                            if (T_List[t].ThreadState == System.Threading.ThreadState.Running)
                            {
                                finished = false;
                                break;
                            }
                        }
                    } while (!finished);
                }
            }

            /// <summary>
            /// The adaptive version of a voxel grid.
            /// </summary>
            /// <param name="Model_in"></param>
            /// <param name="MaxDomain"></param>
            public Voxel_Grid(Topology[] Model_in, int MaxDomain, int Avg_polys)
            {
                //Initialize all variables
                Model = Model_in;
                Point MaxPT = new Point(Double.NegativeInfinity, Double.NegativeInfinity, Double.NegativeInfinity);
                Point MinPT = new Point(Double.PositiveInfinity, Double.PositiveInfinity, Double.PositiveInfinity);
                Poly_Ray_ID = new int[Model.Length, no_of_boxes][]; //bool[Model.Length, System.Environment.ProcessorCount][];

                for (int i = 0; i < Model.Length; i++)
                {
                    for (int p = 0; p < no_of_boxes; p++)
                    {
                        Poly_Ray_ID[i, p] = new int[Model[i].Polygon_Count];
                    }
                }

                //Get the max and min points of each topology...
                for (int m = 0; m < Model.Length; m++)
                {
                    if ((Model[m].Max.x + 0.01) > MaxPT.x) MaxPT.x = (Model[m].Max.x + Epsilon);
                    if ((Model[m].Max.y + 0.01) > MaxPT.y) MaxPT.y = (Model[m].Max.y + Epsilon);
                    if ((Model[m].Max.z + 0.01) > MaxPT.z) MaxPT.z = (Model[m].Max.z + Epsilon);
                    if ((Model[m].Min.x - 0.01) < MinPT.x) MinPT.x = (Model[m].Min.x - Epsilon);
                    if ((Model[m].Min.y - 0.01) < MinPT.y) MinPT.y = (Model[m].Min.y - Epsilon);
                    if ((Model[m].Min.z - 0.01) < MinPT.z) MinPT.z = (Model[m].Min.z - Epsilon);
                }

                OBox = new AABB(MinPT.x - .1, MinPT.y - .1, MinPT.z - .1, MaxPT.x + .1, MaxPT.y + .1, MaxPT.z + .1);

                Voxels = new AABB[1,1,1];
                Voxels[0,0,0] = new AABB(MinPT.x - .1, MinPT.y - .1, MinPT.z - .1, MaxPT.x + .1, MaxPT.y + .1, MaxPT.z + .1);
                Voxel_Inv = new List<int>[1, 1, 1, Model.Length];
                for (int i = 0; i < Model.Length; i++) Voxel_Inv[0, 0, 0, i] = new List<int>();
                BoxDims = new Point(OBox.Max.x - OBox.Min.x, OBox.Max.y - OBox.Min.y, OBox.Max.z - OBox.Min.z);

                for (int i = 0; i < Model.Length; i++)
                {
                    for (int j = 0; j < Model[i].Polygon_Count; j++) Voxel_Inv[0, 0, 0, i].Add(j);
                }

                //Continuously subdivide voxels until either MaxDomain is reached, or the average number of polygons per voxel is reduced to the goal number...
                for (int k = 0; k < MaxDomain; k++)
                {
                    VoxelCtX = 2 * Voxels.GetLength(0);
                    VoxelCtY = 2 * Voxels.GetLength(1);
                    VoxelCtZ = 2 * Voxels.GetLength(2);
                    XYTot = VoxelCtX * VoxelCtY;

                    AABB[,,] Voxels_temp = new AABB[VoxelCtX, VoxelCtY, VoxelCtZ];
                    List<int>[,,,] Voxel_Inv_temp = new List<int>[VoxelCtX, VoxelCtY, VoxelCtZ, Model.Length];

                    VoxelDims = new Point(BoxDims.x / VoxelCtX, BoxDims.y / VoxelCtY, BoxDims.z / VoxelCtZ);
                    VoxelDims_Inv = new Point(1 / VoxelDims.x, 1 / VoxelDims.y, 1 / VoxelDims.z);
                    BoxDims_Inv = new Point(1 / BoxDims.x, 1 / BoxDims.y, 1 / BoxDims.z);

                    Char_Step = (VoxelDims.x < VoxelDims.y) ? ((VoxelDims.x < VoxelDims.z) ? VoxelDims.x : VoxelDims.z) : (VoxelDims.y < VoxelDims.z ? VoxelDims.y : VoxelDims.z);

                    int processorCT = System.Environment.ProcessorCount;

                    for (int m = 0; m < Model.Length; m++)
                    {
                        System.Threading.Thread[] T_List = new System.Threading.Thread[processorCT];
                        for (int P_I = 0; P_I < processorCT; P_I++)
                        {
                            ThreadParams T_ = new ThreadParams(P_I * VoxelCtX / processorCT, (P_I + 1) * VoxelCtX / processorCT, P_I, m);
                            System.Threading.ParameterizedThreadStart TS = new System.Threading.ParameterizedThreadStart(delegate(object T)
                                {
                                   ThreadParams Tp = (ThreadParams)T;
                                    for (int x = Tp.startvoxel; x < Tp.endvoxel; x++)
                                    {
                                        for (int y = 0; y < VoxelCtY; y++)
                                        {
                                            for (int z = 0; z < VoxelCtZ; z++)
                                            {
                                                Voxel_Inv_temp[x, y, z, Tp.m] = new List<int>();
                                                Point VoxelMin = new Point(x * VoxelDims.x - Epsilon, y * VoxelDims.y - Epsilon, z * VoxelDims.z - Epsilon);
                                                Point VoxelMax = new Point((x + 1) * VoxelDims.x + Epsilon, (y + 1) * VoxelDims.y + Epsilon, (z + 1) * VoxelDims.z + Epsilon);
                                                AABB Box = new AABB(VoxelMin + OBox.Min, VoxelMax + OBox.Min);
                                                Voxels_temp[x, y, z] = Box;
                                                int x_prev = (int)Math.Floor((double)x/2), y_prev = (int)Math.Floor((double)y/2), z_prev = (int)Math.Floor((double)z/2);
                                                foreach(int i in Voxel_Inv[x_prev, y_prev, z_prev, Tp.m])
                                                {
                                                    //Check for intersection between voxel x,y,z with Polygon i...
                                                    if (Box.PolyBoxOverlap(Model[Tp.m].Polygon_Vertices(i)))
                                                    {
                                                        Voxel_Inv_temp[x, y, z, Tp.m].Add(i);
                                                    }
                                                }
                                                //Check for Null Voxels
                                                if (Voxel_Inv_temp[x, y, z, Tp.m] == null)
                                                {
                                                    throw new Exception("Whoops... Null Voxels Detected");
                                                }
                                            }
                                        }
                                    }
                                });
                            T_List[P_I] = new System.Threading.Thread(TS);
                            T_List[P_I].Start(T_);
                        }
                        
                        bool finished = false;

                        do
                        {
                            System.Threading.Thread.Sleep(100);
                            finished = true;
                            for (int t = 0; t < T_List.Length; t++)
                            {
                                if (T_List[t].ThreadState == System.Threading.ThreadState.Running)
                                {
                                    finished = false;
                                    break;
                                }
                            }
                        } while (!finished);
                    }

                    Voxels = Voxels_temp;
                    Voxel_Inv = Voxel_Inv_temp;

                    double sum = 0;
                    int ct = 0;
                    for (int m = 0; m < Model.Length; m++) for (int x = 0; x < Voxels.GetLength(0); x++) for (int y = 0; y < Voxels.GetLength(1); y++) for (int z = 0; z < Voxels.GetLength(2); z++) if (Voxel_Inv_temp[x, y, z, m].Count > 0) { sum += Voxel_Inv_temp[x, y, z, m].Count; ct++; }
                    if (k > 1 && sum / ct < Avg_polys) return; //We are done...
                }
            }

            public void VoxelDecode(int Code, out int X, out int Y, out int Z)
            {
                Z = (int)Math.Floor((double)(Code / XYTot));
                Code -= Z * XYTot;
                Y = (int)Math.Floor((double)(Code / VoxelCtY));
                X = Code - Y * VoxelCtY;
            }

            public int VoxelCode(int X, int Y, int Z)
            {
                return XYTot * Z + VoxelCtY * X + Y;
            }

            /// <summary>
            /// Adaptive version
            /// </summary>
            /// <param name="o"></param>
            public void Fill_Voxels(object o)
            {
                ThreadParams T = (ThreadParams)o;
                for (int x = T.startvoxel; x < T.endvoxel; x++)
                {
                    for (int y = 0; y < VoxelCtY; y++)
                    {
                        for (int z = 0; z < VoxelCtZ; z++)
                        {
                            Voxel_Inv[x, y, z, T.m] = new List<int>();
                            Point VoxelMin = new Point(x * VoxelDims.x - Epsilon, y * VoxelDims.y - Epsilon, z * VoxelDims.z - Epsilon);
                            Point VoxelMax = new Point((x + 1) * VoxelDims.x + Epsilon, (y + 1) * VoxelDims.y + Epsilon, (z + 1) * VoxelDims.z + Epsilon);
                            AABB Box = new AABB(VoxelMin + OBox.Min, VoxelMax + OBox.Min);
                            Voxels[x, y, z] = Box;
                            for (int i = 0; i < Model[T.m].Polygon_Count; i++)
                            {
                                //Check for intersection between voxel x,y,z with Polygon i...
                                if (Box.PolyBoxOverlap(Model[T.m].Polygon_Vertices(i)))
                                {
                                    Voxel_Inv[x, y, z, T.m].Add(i);
                                }
                            }
                            //Check for Null Voxels
                            if (Voxel_Inv[x, y, z, T.m] == null)
                            {
                                throw new Exception("Whoops... Null Voxels Detected");
                            }
                            ///////////////////////
                        }
                    }
                }
            }

            private struct ThreadParams
            {
                public int startvoxel;
                public int endvoxel;
                public int threadID;
                public int m;

                public ThreadParams(int start, int end, int thread, int min)
                {
                    startvoxel = start;
                    endvoxel = end;
                    threadID = thread;
                    m = min;
                }
            }

            public void PointInVoxel(Point Pt, out int X, out int Y, out int Z)
            {
                X = (int)Math.Floor((Pt.x - OBox.Min.x) / VoxelDims.x);
                Y = (int)Math.Floor((Pt.y - OBox.Min.y) / VoxelDims.y);
                Z = (int)Math.Floor((Pt.z - OBox.Min.z) / VoxelDims.z);
            }

            public int PointInVoxel(Point Pt)
            {
                return VoxelCode((int)Math.Floor((Pt.x - OBox.Min.x) / VoxelDims.x), (int)Math.Floor((Pt.y - OBox.Min.y) / VoxelDims.y), (int)Math.Floor((Pt.z - OBox.Min.z) / VoxelDims.z));    
            }

            protected uint assign_id()
            {
                lock (ctlock)
                {
                    rayno++;
                    if (rayno == no_of_boxes) { rayno = 0; }
                    return rayno;
                }
            }

            /// <summary>
            /// Fire a ray into the model. Ray must start inside the bounding box of the Topology.
            /// </summary>
            /// <param name="R"> The ray to be entered. Make certain the Ray has a unique Ray_ID variable. </param>
            /// <param name="top_index"> Indicates the topology the ray is to intersect. </param>
            /// <param name="Ret_Event"> The nearest resulting intersection information, if any. </param>
            /// <returns> Indicates whether or not an intersection was found. </returns>
            public override bool Shoot(Ray R, int top_index, out X_Event Ret_Event, int poly_origin1, int poly_origin2 = -1)
            {
                uint rayid = assign_id();

                int X, Y, Z;
                //Identify which voxel the Origin point is located in...
                X = (int)Math.Floor((R.x - OBox.Min.x) / VoxelDims.x);
                Y = (int)Math.Floor((R.y - OBox.Min.y) / VoxelDims.y);
                Z = (int)Math.Floor((R.z - OBox.Min.z) / VoxelDims.z);

                double tDeltaX, tDeltaY, tDeltaZ;
                double tMaxX = 0, tMaxY = 0, tMaxZ = 0;

                int stepX, stepY, stepZ, OutX, OutY, OutZ;
                double t_start = 0;

                if (X < 0 || X >= VoxelCtX || Y < 0 || Y >= VoxelCtY || Z < 0 || Z >= VoxelCtZ) //return false;
                {
                    if (!OBox.Intersect(ref R, ref t_start))
                    {
                        Ret_Event = new X_Event();
                        return false;
                    }
                    X = (int)Math.Floor((R.x - OBox.Min.x + R.dx * 1E-6) / VoxelDims.x);
                    Y = (int)Math.Floor((R.y - OBox.Min.y + R.dy * 1E-6) / VoxelDims.y);
                    Z = (int)Math.Floor((R.z - OBox.Min.z + R.dz * 1E-6) / VoxelDims.z);
                }

                if (R.dx < 0)
                {
                    OutX = -1;
                    stepX = -1;
                    tMaxX = (Voxels[X, Y, Z].Min.x - R.x) / R.dx;
                    tDeltaX = VoxelDims.x / R.dx * stepX;
                }
                else
                {
                    OutX = VoxelCtX;
                    stepX = 1;
                    tMaxX = (Voxels[X, Y, Z].Max.x - R.x) / R.dx;
                    tDeltaX = VoxelDims.x / R.dx * stepX;
                }

                if (R.dy < 0)
                {
                    OutY = -1;
                    stepY = -1;
                    tMaxY = (Voxels[X, Y, Z].Min.y - R.y) / R.dy;
                    tDeltaY = VoxelDims.y / R.dy * stepY;
                }
                else
                {
                    OutY = VoxelCtY;
                    stepY = 1;
                    tMaxY = (Voxels[X, Y, Z].Max.y - R.y) / R.dy;
                    tDeltaY = VoxelDims.y / R.dy * stepY;
                }

                if (R.dz < 0)
                {
                    OutZ = -1;
                    stepZ = -1;
                    tMaxZ = (Voxels[X, Y, Z].Min.z - R.z) / R.dz;
                    tDeltaZ = VoxelDims.z / R.dz * stepZ;
                }
                else
                {
                    OutZ = VoxelCtZ;
                    stepZ = 1;
                    tMaxZ = (Voxels[X, Y, Z].Max.z - R.z) / R.dz;
                    tDeltaZ = VoxelDims.z / R.dz * stepZ;
                }

                //List<Point> X_LIST = new List<Point>();
                //List<double> ulist = new List<double>();
                //List<double> vlist = new List<double>();
                //List<double> tlist = new List<double>();
                //List<int> pidlist = new List<int>();

                //while (true)
                //{
                //    //Check all polygons in the current voxel...
                //    foreach (int i in Voxel_Inv[X, Y, Z, top_index])
                //    {
                //        if (i == poly_origin1 || i == poly_origin2) continue;
                //        if (Poly_Ray_ID[top_index, rayid][i] != R.Ray_ID)
                //        {
                //            Poly_Ray_ID[top_index, rayid][i] = R.Ray_ID;
                //            Point Pt; double u = 0, v = 0, t = 0;
                //            if (Model[top_index].intersect(i, R, out Pt, out u, out v, out t) && t > 0.0000000001)
                //            {
                //                X_LIST.Add(Pt);
                //                ulist.Add(u);
                //                vlist.Add(v);
                //                tlist.Add(t);
                //                pidlist.Add(i);
                //            }
                //        }
                //    }

                //    for (int c = 0; c < X_LIST.Count; c++)
                //    {
                //        if (this.Voxels[X, Y, Z].IsPointInBox(X_LIST[c].x, X_LIST[c].y, X_LIST[c].z))
                //        {
                //            int choice = c;
                //            for (int s = c + 1; s < X_LIST.Count; s++)
                //            {
                //                if (tlist[s] < tlist[choice])
                //                {
                //                    choice = s;
                //                }
                //            }
                //            Ret_Event = new X_Event(X_LIST[choice], ulist[choice], vlist[choice], tlist[choice] + t_start, pidlist[choice]);
                //            return true;
                //        }
                //    }

                Point Xpt = null;
                double umin = 0, vmin = 0, tmin = double.MaxValue;
                int pid = -1;

                while (true)
                {
                    //Check all polygons in the current voxel...
                    foreach (int i in Voxel_Inv[X, Y, Z, top_index])
                    {
                        if (i == poly_origin1 || i == poly_origin2) continue;
                        if (Poly_Ray_ID[top_index, rayid][i] != R.Ray_ID)
                        {
                            Poly_Ray_ID[top_index, rayid][i] = R.Ray_ID;
                            double x, y, z, t;
                            if (Model[top_index].intersect(i, ref R, out x, out y, out z, out t) && t > 0.0000000001)
                            {
                                if (t < tmin)
                                {
                                    Xpt = new Point(x, y, z);
                                    umin = 0;
                                    vmin = 0;
                                    tmin = t;
                                    pid = i;
                                }
                            }
                        }
                    }

                    if (Xpt != null && this.Voxels[X, Y, Z].IsPointInBox(Xpt.x, Xpt.y, Xpt.z))
                    {
                        Ret_Event = new X_Event(Xpt, umin, vmin, tmin + t_start, pid);
                        return true;
                    }

                    //Find the Next Voxel...                    
                    /////////////////////////////////////////////////
                    if (tMaxX < tMaxY)
                    {
                        if (tMaxX < tMaxZ)
                        {
                            X += stepX;
                            if (X < 0 || X >= VoxelCtX)
                            {
                                Ret_Event = new X_Event();
                                return false; /* outside grid */
                            }
                            tMaxX = tMaxX + tDeltaX;
                        }
                        else
                        {
                            Z += stepZ;

                            if (Z < 0 || Z >= VoxelCtZ)
                            {
                                Ret_Event = new X_Event();
                                return false; /* outside grid */
                            }
                            tMaxZ = tMaxZ + tDeltaZ;
                        }
                    }
                    else
                    {
                        if (tMaxY < tMaxZ)
                        {
                            Y += stepY;
                            if (Y < 0 || Y >= VoxelCtY)
                            {
                                Ret_Event = new X_Event();
                                return false; /* outside grid */
                            }
                            tMaxY = tMaxY + tDeltaY;
                        }
                        else
                        {
                            Z += stepZ;
                            if (Z < 0 || Z >= VoxelCtZ)
                            {
                                Ret_Event = new X_Event();
                                return false; /* outside grid */
                            }
                            tMaxZ = tMaxZ + tDeltaZ;
                        }
                    }
                }
            }

            /// <summary>
            /// Fire a ray into the model. Ray must start inside the bounding box of the Topology.
            /// </summary>
            /// <param name="R"> The ray to be entered. Make certain the Ray has a unique Ray_ID variable. </param>
            /// <param name="top_index"> Indicates the topology the ray is to intersect. </param>
            /// <param name="Ret_Event"> The nearest resulting intersection information, if any. </param>
            /// <returns> Indicates whether or not an intersection was found. </returns>
            public override bool Shoot(Ray R, int top_index, out X_Event Ret_Event)
            {
                uint rayid = assign_id();

                int X, Y, Z;
                //Identify which voxel the Origin point is located in...
                X = (int)Math.Floor((R.x - OBox.Min.x) / VoxelDims.x);
                Y = (int)Math.Floor((R.y - OBox.Min.y) / VoxelDims.y);
                Z = (int)Math.Floor((R.z - OBox.Min.z) / VoxelDims.z);

                double tDeltaX, tDeltaY, tDeltaZ;
                double tMaxX = 0, tMaxY = 0, tMaxZ = 0;

                int stepX, stepY, stepZ, OutX, OutY, OutZ;
                double t_start = 0;

                if (X < 0 || X >= VoxelCtX || Y < 0 || Y >= VoxelCtY || Z < 0 || Z >= VoxelCtZ) //return false;
                {
                    if (!OBox.Intersect(ref R, ref t_start))
                    {
                        Ret_Event = new X_Event();
                        return false;
                    }
                    X = (int)Math.Floor((R.x - OBox.Min.x + R.dx * 1E-6) / VoxelDims.x);
                    Y = (int)Math.Floor((R.y - OBox.Min.y + R.dy * 1E-6) / VoxelDims.y);
                    Z = (int)Math.Floor((R.z - OBox.Min.z + R.dz * 1E-6) / VoxelDims.z);
                }

                if (R.dx < 0)
                {
                    OutX = -1;
                    stepX = -1;
                    tMaxX = (Voxels[X, Y, Z].Min.x - R.x) / R.dx;
                    tDeltaX = VoxelDims.x / R.dx * stepX;
                }
                else
                {
                    OutX = VoxelCtX;
                    stepX = 1;
                    tMaxX = (Voxels[X, Y, Z].Max.x - R.x) / R.dx;
                    tDeltaX = VoxelDims.x / R.dx * stepX;
                }

                if (R.dy < 0)
                {
                    OutY = -1;
                    stepY = -1;
                    tMaxY = (Voxels[X, Y, Z].Min.y - R.y) / R.dy;
                    tDeltaY = VoxelDims.y / R.dy * stepY;
                }
                else
                {
                    OutY = VoxelCtY;
                    stepY = 1;
                    tMaxY = (Voxels[X, Y, Z].Max.y - R.y) / R.dy;
                    tDeltaY = VoxelDims.y / R.dy * stepY;
                }

                if (R.dz < 0)
                {
                    OutZ = -1;
                    stepZ = -1;
                    tMaxZ = (Voxels[X, Y, Z].Min.z - R.z) / R.dz;
                    tDeltaZ = VoxelDims.z / R.dz * stepZ;
                }
                else
                {
                    OutZ = VoxelCtZ;
                    stepZ = 1;
                    tMaxZ = (Voxels[X, Y, Z].Max.z - R.z) / R.dz;
                    tDeltaZ = VoxelDims.z / R.dz * stepZ;
                }

                //List<Point> X_LIST = new List<Point>();
                //List<double> ulist = new List<double>();
                //List<double> vlist = new List<double>();
                //List<double> tlist = new List<double>();
                //List<int> pidlist = new List<int>();

                //while (true)
                //{
                //    //Check all polygons in the current voxel...
                //    foreach (int i in Voxel_Inv[X, Y, Z, top_index])
                //    {
                //        if (i == poly_origin1 || i == poly_origin2) continue;
                //        if (Poly_Ray_ID[top_index, rayid][i] != R.Ray_ID)
                //        {
                //            Poly_Ray_ID[top_index, rayid][i] = R.Ray_ID;
                //            Point Pt; double u = 0, v = 0, t = 0;
                //            if (Model[top_index].intersect(i, R, out Pt, out u, out v, out t) && t > 0.0000000001)
                //            {
                //                X_LIST.Add(Pt);
                //                ulist.Add(u);
                //                vlist.Add(v);
                //                tlist.Add(t);
                //                pidlist.Add(i);
                //            }
                //        }
                //    }

                //    for (int c = 0; c < X_LIST.Count; c++)
                //    {
                //        if (this.Voxels[X, Y, Z].IsPointInBox(X_LIST[c].x, X_LIST[c].y, X_LIST[c].z))
                //        {
                //            int choice = c;
                //            for (int s = c + 1; s < X_LIST.Count; s++)
                //            {
                //                if (tlist[s] < tlist[choice])
                //                {
                //                    choice = s;
                //                }
                //            }
                //            Ret_Event = new X_Event(X_LIST[choice], ulist[choice], vlist[choice], tlist[choice] + t_start, pidlist[choice]);
                //            return true;
                //        }
                //    }

                Point Xpt = null;
                double umin = 0, vmin = 0, tmin = double.MaxValue;
                int pid = -1;

                while (true)
                {
                    //Check all polygons in the current voxel...
                    foreach (int i in Voxel_Inv[X, Y, Z, top_index])
                    {
                        if (Poly_Ray_ID[top_index, rayid][i] != R.Ray_ID)
                        {
                            Poly_Ray_ID[top_index, rayid][i] = R.Ray_ID;
                            double x, y, z, t;
                            if (Model[top_index].intersect(i, ref R, out x, out y, out z, out t) && t > 0.0000000001)
                            {
                                if (t < tmin)
                                {
                                    Xpt = new Point(x, y, z);
                                    umin = 0;
                                    vmin = 0;
                                    tmin = t;
                                    pid = i;
                                }
                            }
                        }
                    }

                    if (Xpt != null && this.Voxels[X, Y, Z].IsPointInBox(Xpt.x, Xpt.y, Xpt.z))
                    {
                        Ret_Event = new X_Event(Xpt, umin, vmin, tmin + t_start, pid);
                        return true;
                    }

                    //Find the Next Voxel...                    
                    /////////////////////////////////////////////////
                    if (tMaxX < tMaxY)
                    {
                        if (tMaxX < tMaxZ)
                        {
                            X += stepX;
                            if (X < 0 || X >= VoxelCtX)
                            {
                                Ret_Event = new X_Event();
                                return false; /* outside grid */
                            }
                            tMaxX = tMaxX + tDeltaX;
                        }
                        else
                        {
                            Z += stepZ;

                            if (Z < 0 || Z >= VoxelCtZ)
                            {
                                Ret_Event = new X_Event();
                                return false; /* outside grid */
                            }
                            tMaxZ = tMaxZ + tDeltaZ;
                        }
                    }
                    else
                    {
                        if (tMaxY < tMaxZ)
                        {
                            Y += stepY;
                            if (Y < 0 || Y >= VoxelCtY)
                            {
                                Ret_Event = new X_Event();
                                return false; /* outside grid */
                            }
                            tMaxY = tMaxY + tDeltaY;
                        }
                        else
                        {
                            Z += stepZ;
                            if (Z < 0 || Z >= VoxelCtZ)
                            {
                                Ret_Event = new X_Event();
                                return false; /* outside grid */
                            }
                            tMaxZ = tMaxZ + tDeltaZ;
                        }
                    }
                }
            }

            public double Xdim
            {
                get 
                {
                    return BoxDims.x;
                }
            }
            public double Ydim
            {
                get
                {
                    return BoxDims.y;
                }
            }
            public double Zdim
            {
                get
                {
                    return BoxDims.z;
                }
            }

            public Point MinPt
            {
                get 
                {
                    return OBox.Min_PT;
                }
            }
        }
    }
}