using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.ObjectBuilders;
using VRageMath;

namespace SEScripts.DeadMansSwitch_1_0_186
{
	public class Program : MyGridProgram
	{
		// delay before execution of dead man's switch code
		// 15 seconds by default
		public const int DELAY = RUNS_PES_SECOND * 15;

		private const int RUNS_PES_SECOND = 6;
		private int _cycleNumber = 0;
		private int _countdown = DELAY;

		public Program()
		{
			Runtime.UpdateFrequency = UpdateFrequency.Update10;
		}

		public void Save()
		{ }

		public void Main(string argument, UpdateType updateSource)
		{
			PrintStatus();
			DoJob();
			PrintOnlineStatus();
		}

		private void PrintStatus()
		{
			Echo("Dead man's switch is on");
		}

		private void DoJob()
		{
			bool isNoShipControllerFound;
			if (IsShipSafe(out isNoShipControllerFound))
			{
				ResetCountdown();
				if (isNoShipControllerFound)
				{
					Echo("Cannot find any dampeners steering");
					Echo("Cannot protect the ship");
				}
				else
				{
					Echo("The ship is safe");
					Echo("Dampeners are on");
				}
				return;
			}

			if (IsShipUnderControl())
			{
				Echo("The ship is unsafe, but under control");
				return;
			}

			if (ProgressCountdown())
			{
				Echo("The ship is unsafe and unattended");
				Echo("Executing dead man's switch in " + _countdown);
			}
			else
			{
				Echo("Executing dead man's switch...");
				ExecuteDeadMansSwitch();
			}
		}

		private bool IsShipSafe(out bool isNoShipControllerFound)
		{
			var list = new List<IMyShipController>();
			GridTerminalSystem.GetBlocksOfType(list, q => q.ControlThrusters);

			if (list.Count == 0)
			{
				isNoShipControllerFound = false;
				return true;
			}
		}

		private bool IsShipBroken()
		{
			throw new NotImplementedException();
		}

		private bool IsShipUnderControl()
		{
			List<IMyTerminalBlock> list = new List<IMyTerminalBlock>();
			GridTerminalSystem.GetBlocksOfType<IMyShipController>(list);
			List<string> Active = new List<string>();
			for (int e = 0; e < list.Count; e++)
			{
				IMyShipController block = list[e] as IMyShipController;
				if (block.IsUnderControl)
				{
					if (!block.BlockDefinition.ToString().Contains("CryoChamber") && !block.BlockDefinition.ToString().Contains("PassengerSeat"))
					{ Active.Add(block.CustomName); }
				}
			}
			if (Active.Count != 0)
			{ Echo("Active Pilot Systems"); for (int e = 0; e < Active.Count; e++) { Echo("#" + (e + 1) + " " + Active[e]); } return true; }
			else
			{ return false; }
		}

		private bool ProgressCountdown()
		{
			if (_countdown > 0)
			{
				_countdown -= 1;
				return true;
			}
			return false;
		}

		private void ExecuteDeadMansSwitch()
		{
			throw new NotImplementedException();
		}

		private void ResetCountdown()
		{
			_countdown = DELAY;
		}

		private void PrintOnlineStatus()
		{
			var tab = new char[42];
			for (int i = 0; i < 42; ++i)
			{
				char c = ' ';
				if (i % 21 == 1)
					c = '|';
				else if (i % 7 < 4)
					c = '·';
				tab[41 - (i + _cycleNumber) % 42] = c;
			}
			Echo(new String(tab));
			++_cycleNumber;
		}

		// This script will turn on all thrusters (in case they are off)
		// and toggle inertial dampeners to stop the ship when run.
		//
		// This script can be used in conjunction with a sensor near the
		// cockpit to detect the pilot if he exits the vessel while
		// moving. This will have the effect of stopping the vessel
		// if the pilot accidentally exits the cockpit while in
		// motion.
		//
		// Alternatively, you can create a timer block to run this script
		// on a delay (i.e. 60 seconds). If the timer runs out, the script
		// will run and stop the ship. You can set a toolbar key to "Start"
		// the timer. That way, the pilot has to press the toolbar key
		// before the timer runs out or the ship will stop. This functions
		// as a "dead man's switch" to stop the ship in the case the player
		// gets disconnected.
		private void Main()
		{
			// Find all thruster blocks on the ship
			List<IMyTerminalBlock> thrusters = GetThrusters();

			// Loop through all thruster blocks and turn them on
			// (in case they are off)
			for (var i = 0; i < thrusters.Count; i++)
			{
				var thruster = thrusters[i] as IMyThrust;
				if (thruster != null)
				{
					thruster.GetActionWithName("OnOff_On").Apply(thruster);
				}
			}

			// Find all controller blocks (cockpits, flight seats, etc)
			List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
			GridTerminalSystem.GetBlocksOfType<IMyShipController>(blocks);

			// Loop through all found controller blocks
			for (var i = 0; i < blocks.Count; i++)
			{
				var controller = blocks[i] as IMyShipController;

				// If dampeners are not active, turn them on.
				// Unlike the thrusters above, the dampener control
				// is a toggle, so we have to check if it's active
				// before we toggle it, otherwise, if the dampeners
				// are already on, we would turn them off when
				// toggling
				if ((controller != null) && (!controller.DampenersOverride))
				{
					controller.GetActionWithName("DampenersOverride").Apply(controller);
				}
			}
		}

		// Helper function to find and return all thruster blocks
		private List<IMyTerminalBlock> GetThrusters()
		{
			List<IMyTerminalBlock> list = new List<IMyTerminalBlock>();
			GridTerminalSystem.GetBlocksOfType<IMyThrust>(list);
			return list;
		}

		// Script By: MUSTARDMAN24
		// Usage: Copy script into Programming Block, have any sensor, button or other action trigger Run
		// Results:
		//		Thrusters: Powered on, overrides disabled
		//		Cockpit(All Types): Inertial Dampeners Enabled
		//		Gyroscopes: Powered on, overrides disabled, power set to max
		private void Main()
		{
			EmergencyStop();
		}

		private void EmergencyStop()
		{
			DisableThrusterOverrides();
			EnableInertialDampers();
			DisableGyroOverrides();
		}

		private void DisableThrusterOverrides()
		{
			List<IMyThrust> thrusters = new List<IMyThrust>();
			List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
			GridTerminalSystem.GetBlocksOfType<IMyThrust>(blocks);

			for (int i = 0; i < blocks.Count; i++)
			{
				thrusters.Add(blocks[i] as IMyThrust);
			}
			for (int i = 0; i < thrusters.Count; i++)
			{
				while (thrusters[i].ThrustOverride != 0)
				{
					thrusters[i].GetActionWithName("DecreaseOverride").Apply(thrusters[i]);
				}
				thrusters[i].GetActionWithName("OnOff_On").Apply(thrusters[i]);
			}
		}

		private void EnableInertialDampers()
		{
			List<IMyCockpit> controlStations = new List<IMyCockpit>();
			List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
			GridTerminalSystem.GetBlocksOfType<IMyCockpit>(blocks);

			for (int i = 0; i < blocks.Count; i++)
			{
				controlStations.Add(blocks[i] as IMyCockpit);
			}
			for (int i = 0; i < controlStations.Count; i++)
			{
				if (controlStations[i].DampenersOverride == false)
				{
					controlStations[i].GetActionWithName("DampenersOverride").Apply(controlStations[i]);
				}
			}
		}

		private void DisableGyroOverrides()
		{
			List<IMyGyro> gyros = new List<IMyGyro>();
			List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
			GridTerminalSystem.GetBlocksOfType<IMyGyro>(blocks);
			for (int i = 0; i < blocks.Count; i++)
			{
				gyros.Add(blocks[i] as IMyGyro);
			}

			for (int i = 0; i < gyros.Count; i++)
			{
				gyros[i].GetActionWithName("OnOff_On").Apply(gyros[i]);
				if (gyros[i].GyroOverride)
				{
					gyros[i].GetActionWithName("Override").Apply(gyros[i]);
				}
				while (gyros[i].GyroPower < 1.0)
				{
					gyros[i].GetActionWithName("IncreasePower").Apply(gyros[i]);
				}
			}
		}

		/* * Cats makes the best programmers! */ // returned true if there is an active cockpit or remote control block public bool isPiloted() { List<IMyTerminalBlock> list = new List<IMyTerminalBlock>(); // IMyCockpit would exclude Remote Control blocks. (Cryo pods and passenger seats are manually filtered from this search) GridTerminalSystem.GetBlocksOfType<IMyShipController>(list); List<string> Active = new List<string>(); for( int e = 0; e < list.Count; e++ ) { IMyShipController block = list[e] as IMyShipController; if( block.IsUnderControl ) { if( !block.BlockDefinition.ToString().Contains( "CryoChamber" ) && !block.BlockDefinition.ToString().Contains( "PassengerSeat" ) ) { Active.Add( block.CustomName ); } } } if( Active.Count != 0 ) { Echo( "Active Pilot Systems" ); for( int e = 0; e < Active.Count; e++ ) { Echo( "#"+ (e+1) +" "+ Active[e] ); } return true; } else { return false; } } public void Main( string strIn ) { Echo( "ET: "+ ElapsedTime ); Echo( "CAT - Cockpit Check" ); Echo( "IsPiloted: "+ isPiloted() ); }

		/* Script By: Silver Version: 1.03 About: This script will stop a runaway ship. This script stops the ship when it is not being piloted and not being remote controlled. This is useful when flying an expensive ship in multiplayer with no dampeners or with gravity thrusters and then when you crash/dc the ship would fly forever until it randomly hits an Asteroid. Even with a medbay it's hard to get in and stop it manually. Instructions: Copy this script into a programming block and set a timer to trigger this block every 1 second. Also don't forget to make the timer restart itself. Optional: Run with ANY argument to force the script to stop the ship. */ // Settings: const bool DisableArtificialMass = true; const bool DisableGravityEngines = false; const bool TurnOnDampeners = true; const bool SetThrusters = true; // Enable & remove overrides const bool SetGyros = true; // Enable and maximize power //////////////////////////////////////////////////////////////////////////////////////////////////////////////// // DO NOT EDIT BELOW THIS LINE! //////////////////////////////////////////////////////////////////////////////////////////////////////////////// void Main(string forceStop) { if ((forceStop != "") || !isPiloted()) HitTheBrakes(); } void HitTheBrakes() { if (DisableArtificialMass) { DisableArtificialMassBlocks(); } if(DisableGravityEngines) { DisableGravThrusterBlocks(); } if (TurnOnDampeners) { EnableDampers(); } if (SetThrusters) { Thrusters(); } if (SetGyros) { Gyros(); } } void DisableArtificialMassBlocks() { List<IMyTerminalBlock> artiMass = new List<IMyTerminalBlock>(); GridTerminalSystem.GetBlocksOfType<IMyVirtualMass>(artiMass); for (int i=0; i<artiMass.Count; i++) { artiMass.GetActionWithName("OnOff_Off").Apply(artiMass);

		private void DisableGravThrusterBlocks()
		{
			List<IMyTerminalBlock> gravGens = new List<IMyTerminalBlock>();
			GridTerminalSystem.GetBlocksOfType<IMyGravityGenerator>(gravGens);
			for (int i = 0; i < gravGens.Count; i++)
			{
				gravGens.GetActionWithName("OnOff_Off").Apply(gravGens);
			}
		}

		private void Thrusters()
		{
			List<IMyTerminalBlock> thrusters = new List<IMyTerminalBlock>();
			GridTerminalSystem.GetBlocksOfType<IMyThrust>(thrusters);

			for (int i = 0; i < thrusters.Count; i++)
			{
				thrusters.GetActionWithName("OnOff_On").Apply(thrusters);
				while (((IMyThrust)thrusters).ThrustOverride > 0)
					thrusters.GetActionWithName("DecreaseOverride").Apply(thrusters);
			}
		}

		private void EnableDampers()
		{
			List<IMyTerminalBlock> cockpits = new List<IMyTerminalBlock>();
			GridTerminalSystem.GetBlocksOfType<IMyCockpit>(cockpits);

			for (int i = 0; i < cockpits.Count; i++)
			{
				if (!((IMyCockpit)cockpits).DampenersOverride)
					cockpits.GetActionWithName("DampenersOverride").Apply(cockpits);
			}
		}

		private void Gyros()
		{
			List<IMyTerminalBlock> gyros = new List<IMyTerminalBlock>();
			GridTerminalSystem.GetBlocksOfType<IMyGyro>(gyros);

			for (int i = 0; i < gyros.Count; i++)
			{
				gyros.GetActionWithName("OnOff_On").Apply(gyros);

				while (((IMyGyro)gyros).GyroPower < 1.0)
					gyros.GetActionWithName("IncreasePower").Apply(gyros);
			}
		}

		// returned true if there is an active cockpit or remote control block
		// Note: this function was not written by me.
		public bool isPiloted()
		{
			List<IMyTerminalBlock> list = new List<IMyTerminalBlock>();
			// IMyCockpit would exclude Remote Control blocks. (Cryo pods and passenger seats are manually filtered from this search)
			GridTerminalSystem.GetBlocksOfType<IMyShipController>(list);
			List<string> Active = new List<string>();
			for (int e = 0; e < list.Count; e++)
			{
				IMyShipController block = list[e] as IMyShipController;
				if (block.IsUnderControl)
				{
					if (!block.BlockDefinition.ToString().Contains("CryoChamber") && !block.BlockDefinition.ToString().Contains("PassengerSeat"))
					{
						Active.Add(block.CustomName);
					}
				}
			}
			if (Active.Count != 0)
			{
				//Echo( "Active Pilot Systems" );
				//for( int e = 0; e < Active.Count; e++ ) {
				//    Echo( "#"+ (e+1) +" "+ Active[e] );
				//}
				return true;
			}
			else
			{
				return false;
			}
		}
	}
}