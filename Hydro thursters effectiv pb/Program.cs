using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        // By：Schwarzengel version 1.0
        // Comand：Planet Cruise, Normal, Space, Landing
        // Cockpit name: Main Cockpit
        // Timerblock name: Hycontrol Space Mode, add every thing you want to trigger in space mode ,it will be triggered once
        // LCD name: Hy/Fan Thruster Controll System LCD
        // LCD AND TIMERBLOCK ARE OPTIONAL
        //以下3个参数可以自定义 // The following 3 parameters can be customized
        int minHydrogenThrusters = 1; // 最低氢气推进器限制 // Minimum hydrogen thruster limit to activate when the ship goes down automaticly, without 'wasd'
        int n_correctionhydrogenThrusters = 15; // 每次纠正时打开的氢气推进器数量 // Number of hydrogen thrusters to activate during each correction when the ship goes down automaticly, at cruise mode with 'wasd'  
        string cockpitname = "Main Cockpit"; // 驾驶舱名称 // Cockpit name


        //以下参数不可以自定义  The following parameters cant be customized
        private IMyCockpit mainCockpit;
        private List<IMyThrust> hydrogenUpThrusters;
        private List<IMyThrust> thrustList;
        private List<IMyThrust> atmosphericUpThrusters;
        private List<IMyThrust> ionUpThrusters;
        private string currentCommand = ""; // 当前命令 
        private bool spaceModeTriggered = false; // Space模式触发标志
        private IMyTextPanel lcdPanel; // LCD面板
        private List<IMyGasTank> hydrogenTanks; // 氢气罐列表


        public Program()
       {
           // 设置脚本每10帧触发一次
           Runtime.UpdateFrequency = UpdateFrequency.Update10;

           // 获取主驾驶座
           mainCockpit = GridTerminalSystem.GetBlockWithName(cockpitname) as IMyCockpit;

           // 初始化推进器列表
           hydrogenUpThrusters = new List<IMyThrust>();
           atmosphericUpThrusters = new List<IMyThrust>();
           ionUpThrusters = new List<IMyThrust>();

           // 获取所有推进器
           thrustList = new List<IMyThrust>();
           GridTerminalSystem.GetBlocksOfType(thrustList);

           // 遍历并分类推进器
           foreach (var thruster in thrustList)
           {
               string subtypeId = thruster.BlockDefinition.SubtypeId;

               if (thruster.Orientation.Forward == Base6Directions.Direction.Up)
               {
                   if (subtypeId.Contains("Hydrogen"))
                   {
                       // 将氢推进器加入氢推进器列表
                       hydrogenUpThrusters.Add(thruster);
                   }
                   else if (subtypeId.Contains("Atmospheric"))
                   {
                       // 将大气推进器加入大气推进器列表
                       atmosphericUpThrusters.Add(thruster);
                   }
                   else if (subtypeId.Contains("Thrust")) // 离子推进器包含 "Thrust" 且没有 "Hydrogen" 或 "Atmospheric"
                   {
                       // 将离子推进器加入离子推进器列表
                       ionUpThrusters.Add(thruster);
                   }
               }
           }
       }

        public void Main(string argument, UpdateType updateSource)
        {
          

            // 检查主驾驶座是否存在
            if (mainCockpit == null)
            {
                Echo("Main Cockpit not found");
                return;
            }

            // 检查大气推进器是否存在
            if (atmosphericUpThrusters.Count == 0)
            {
                Echo("Atmospheric Thrusters not found");
                return;
            }
            else
            {
                Echo($"Atmospheric Thrusters found: {atmosphericUpThrusters.Count}");
            }

            // 检查氢气推进器是否存在
            if (hydrogenUpThrusters.Count == 0)
            {
                Echo("Hydrogen Thrusters not found");
                return;
            }
            else
            {
                Echo($"Hydrogen Thrusters found: {hydrogenUpThrusters.Count}");
            }

            // 计算大气推进器的总推力
            float totalAtmosphericUpThrust = atmosphericUpThrusters.Sum(thrust => thrust.MaxEffectiveThrust);

            // 初始化总推力
            float totalUpThrust = totalAtmosphericUpThrust;

            // 如果存在离子推进器，计算离子推进器的总推力并更新总推力
            if (ionUpThrusters.Count > 0)
            {
                float totalIonUpThrust = ionUpThrusters.Sum(thrust => thrust.MaxEffectiveThrust);
                totalUpThrust += totalIonUpThrust;
                Echo($"Ion Thrusters found: {ionUpThrusters.Count}");
            }
            Echo($"Total Up Thrust without hydrogene: {totalUpThrust}");
            // 获取总质量
            float totalMass = mainCockpit.CalculateShipMass().TotalMass;

            // 获取星球重力加速度
            Vector3D gravity = mainCockpit.GetNaturalGravity();
            float gravityAcceleration = (float)gravity.Length();

            // 计算总质量在星球重力下的力（单位：kN）
            float totalWeight = totalMass * gravityAcceleration ;
            Echo($"Total Weight: {totalWeight}");
            Echo("All ready");

            // 如果接收到新的命令，则更新当前命令
            if (!string.IsNullOrEmpty(argument))
            {
                currentCommand = argument;
                spaceModeTriggered = false; // 重置Space模式触发标志
            }

            // 判断编程块命令
            if (currentCommand == "Planet Cruise")
            {
                HandlePlanetMode(totalUpThrust, totalWeight);
                Echo("Planet Cruise");
            }
            else if (currentCommand == "Normal")
            {
                HandleNormalMode();
                Echo("Landing Mode");
            }
            else if (currentCommand == "Space")
            {
                HandleSpaceMode();
                Echo("Space Mode");
            }
            else if (currentCommand == "Landing")
            {
                HandleLandingMode();
                Echo("Landing Mode");
            }

            // 更新LCD显示
            UpdateLCD();
        }

        //星球巡航模式
        private void HandlePlanetMode(float totalUpThrust, float totalWeight)
        {
            // 判断是否接收到来自主控位置的向上或者向下的指令
            bool isMovingUpOrDown = mainCockpit.MoveIndicator.Y != 0;
            Echo($"Moving Up or Down: {isMovingUpOrDown}");

            if (!isMovingUpOrDown)
            {
                // 判断总推力是否大于总质量
                if (totalUpThrust > totalWeight)
                {
                    // 关闭所有向上的氢气推进器
                    foreach (var thrust in hydrogenUpThrusters)
                    {
                        thrust.Enabled = false;
                    }
                    Echo("totalUpThrust > totalWeight");
                }
                else
                {
                    // 将大气推进器的推力开到最大
                    foreach (var thrust in atmosphericUpThrusters)
                    {
                        thrust.ThrustOverridePercentage = 1.0f;
                    }
                  
                    // 计算需要的氢气推进器数量
                    float requiredHydrogenThrust = totalWeight - totalUpThrust;
                    float currentHydrogenThrust = hydrogenUpThrusters.Sum(thrust => thrust.MaxEffectiveThrust);
                    //int requiredHydrogenThrusters = (int)Math.Ceiling(requiredHydrogenThrust / hythrust);
                    // 计算每个氢气推进器的平均推力
                    float averageHydrogenThrust = currentHydrogenThrust / hydrogenUpThrusters.Count;
                    // 计算需要的氢气推进器数量
                    int requiredHydrogenThrusters = (int)Math.Ceiling(requiredHydrogenThrust / averageHydrogenThrust);

                    Echo($"Required Hydrogen Thrusters: {requiredHydrogenThrusters}");

                    if (requiredHydrogenThrusters <= hydrogenUpThrusters.Count - minHydrogenThrusters)
                    {
                        // 获取当前速度
                        Vector3D velocity = mainCockpit.GetShipVelocities().LinearVelocity;
                        float verticalSpeed = (float)velocity.Y;

                        if (verticalSpeed > -0.5)
                        {
                            // 关闭多余的氢气推进器
                            for (int i = 0; i < hydrogenUpThrusters.Count - requiredHydrogenThrusters - minHydrogenThrusters; i++)
                            {
                                hydrogenUpThrusters[i].Enabled = false;
                            }
                        }
                        else
                        {
                            // 先打开所有向上的氢气推进器
                            foreach (var thrust in hydrogenUpThrusters)
                            {
                                thrust.Enabled = true;
                            }

                            // 关闭较少的氢气推进器，扣除修正数n_correctionhydrogenThrusters
                            int thrustersToDisable = hydrogenUpThrusters.Count - requiredHydrogenThrusters - minHydrogenThrusters - n_correctionhydrogenThrusters;
                            for (int i = 0; i < Math.Max(thrustersToDisable, 0); i++)
                            {
                                hydrogenUpThrusters[i].Enabled = false;
                            }
                        }
                        
                    }
                
                }
            }
            else
            {
                // 取消大气推进器的推力越级
                foreach (var thrust in atmosphericUpThrusters)
                {
                    thrust.ThrustOverridePercentage = 0.0f;
                }

                // 打开所有向上的氢气推进器
                foreach (var thrust in hydrogenUpThrusters)
                {
                    thrust.Enabled = true;
                }
            }
        }

        // 正常模式
        private void HandleNormalMode()
        {
            // 取消大气推进器的推力越级
            foreach (var thrust in atmosphericUpThrusters)
            {
                thrust.ThrustOverridePercentage = 0.0f;
            }

            // 打开所有向上的氢气推进器
            foreach (var thrust in hydrogenUpThrusters)
            {
                thrust.Enabled = true;
            }
        }

        // 停泊模式
        private void HandleLandingMode()
        {
            // 取消大气推进器的推力越级
            foreach (var thrust in atmosphericUpThrusters)
            {
                thrust.ThrustOverridePercentage = 0.0f;
            }

            // 关闭所有向上的氢气推进器
            foreach (var thrust in hydrogenUpThrusters)
            {
                thrust.Enabled = false;
            }
        }

        // 太空模式
        private void HandleSpaceMode()
        {
            if (!spaceModeTriggered)
            {
                var timerBlock = GridTerminalSystem.GetBlockWithName("Hycontrol Space Mode") as IMyTimerBlock;
                if (timerBlock != null)
                {
                    timerBlock.Trigger();
                    spaceModeTriggered = true;
                    Echo("Hycontrol Space Mode timer triggered.");
                }
                else
                {
                    Echo("Hycontrol Space Mode timer not found.");
                }
            }
        }

        private void UpdateLCD()
        {
            // 更新主要LCD显示
            if (lcdPanel == null)
            {
                lcdPanel = GridTerminalSystem.GetBlockWithName("Hy/Fan Thruster Controll System LCD") as IMyTextPanel;
            }

            if (lcdPanel != null)
            {
                StringBuilder lcdContent = new StringBuilder();

                // 显示当前模式
                lcdContent.AppendLine($"Current Mode: {currentCommand}");
                lcdContent.AppendLine();

                // 获取氢气罐信息，我想应该没有哪个人不带氢气罐用这个程序吧，所以这里不做判断    
                hydrogenTanks = new List<IMyGasTank>();
                GridTerminalSystem.GetBlocksOfType(hydrogenTanks, tank => tank.BlockDefinition.SubtypeId.Contains("Hydrogen"));

                if (hydrogenTanks.Count == 0)
                {
                    lcdContent.AppendLine("ERRO! Tanks go to heaven ");
                    lcdPanel.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
                    lcdPanel.WriteText(lcdContent.ToString());
                    return;
                }

                else
                {
                    float totalHydrogen = hydrogenTanks.Sum(tank => (float)tank.FilledRatio);
                    float averageHydrogen = totalHydrogen / hydrogenTanks.Count;//计算平均氢气水平，用于显示百分比

                    // 计算氢气总量
                    float totalHydrogenAmount = hydrogenTanks.Sum(tank => (float)tank.Capacity * (float)tank.FilledRatio);
                    // 计算氢气罐的最高储气量
                    float maxHydrogenCapacity = hydrogenTanks.Sum(tank => (float)tank.Capacity);

                    // 获取氢气生成器信息
                    List<IMyGasGenerator> hydrogenGenerators = new List<IMyGasGenerator>();
                    GridTerminalSystem.GetBlocksOfType(hydrogenGenerators);

                    // 计算氢气生成速率
                    float hydrogenGenerationRate = hydrogenGenerators.Sum(generator =>
                    {
                        if (generator.IsProducing)
                        {
                            // 判断方块大小
                            if (generator.CubeGrid.GridSizeEnum == MyCubeSize.Small)
                            {
                                return 100f; // 小船生成器每秒生成100L氢气
                            }
                            else if (generator.CubeGrid.GridSizeEnum == MyCubeSize.Large)
                            {
                                return 500f; // 大船生成器每秒生成500L氢气
                            }
                        }
                        return 0f;
                    });
                    // 输出氢气生成速率
                    Echo($"Hydrogen Generation Rate: {hydrogenGenerationRate} L/s");

                    //lcdContent.AppendLine($"Total Hydrogen Amount: {totalHydrogenAmount:F2} L");//debug用的显示总氢气数值

                    // 计算氢气可使用时间（假设所有氢气罐支持氢气推进器的平均消耗速率）
                    float hydrogenConsumptionRate = hydrogenUpThrusters
                        .Where(thruster => thruster.Enabled)
                        .Sum(thruster => thruster.CurrentThrust * 0.0008f); // 注意单位是thruster.currentthrust 单位是N
                    

                    float hydrogenUsageTime = hydrogenConsumptionRate > 0 ? totalHydrogenAmount / (hydrogenConsumptionRate - hydrogenGenerationRate) / 60 : float.NaN;//创造模式默认氢气生成为1kl/s所以这个功能只有在生存是对的

                    lcdContent.AppendLine($"Hydrogen Tanks: {hydrogenTanks.Count}");
                    lcdContent.AppendLine($"Hydrogen Level: {(averageHydrogen * 100):F2}%");

                    // 显示氢气消耗速率
                    //lcdContent.AppendLine($"Hydrogen Consumption Rate: {hydrogenConsumptionRate:F4} H2/s");//debug用的

                    // 显示氢气可使用时间
                    if (!float.IsNaN(hydrogenUsageTime))
                    {
                        lcdContent.AppendLine($"Hydrogen Usage Time: {hydrogenUsageTime:F2} min");
                    }
                    else
                    {
                        lcdContent.AppendLine($"Hydrogen Usage Time: NAN");
                    }



                    // 计算氢气充满所需时间
                    float hydrogenFillTime = hydrogenGenerationRate > 0 ? (maxHydrogenCapacity - totalHydrogenAmount) / hydrogenGenerationRate / 60 : float.NaN;
                    // 显示氢气充满所需时间
                    if (!float.IsNaN(hydrogenFillTime))
                    {
                        lcdContent.AppendLine($"Hydrogen Fill Time: {hydrogenFillTime:F2} min");
                    }
                    else
                    {
                        lcdContent.AppendLine($"Hydrogen Fill Time: NAN");
                    }

                }
                

                lcdContent.AppendLine();

                // 显示启用的氢气推进器数量
                int activeHydrogenThrusters = hydrogenUpThrusters.Count(thruster => thruster.Enabled);
                lcdContent.AppendLine($"Active Up Hydrogen Thrusters: {activeHydrogenThrusters}");

                // 将内容显示在LCD上
                lcdPanel.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
                lcdPanel.WriteText(lcdContent.ToString());
            }
            else
            {
                Echo("LCD Hy/Fan Thruster Controll System LCD not found.");
            }
        }

    }
}
