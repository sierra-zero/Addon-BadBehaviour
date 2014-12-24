//=============================================================================
// Supporting functions for an AIPlayer driven by a behavior tree
//=============================================================================

datablock PlayerData(BadBotData : DefaultPlayerData)
{
   VisionRange = 40;
   VisionFov = 180;
   findItemRange = 20;
   targetObjectTypes = $TypeMasks::PlayerObjectType;
   itemObjectTypes = $TypeMasks::itemObjectType;
   
   // just some numbers for testing
   optimalRange["Ryder"] = 10;
   optimalRange["Lurker"] = 20;
   rangeTolerance = 5;
   switchTargetProbability = 0.1;
   
   // don't allow quirky weapons
   maxInv[LurkerGrenadeLauncher] = 0;
   maxInv[LurkerGrenadeAmmo] = 0;
   maxInv[ProxMine] = 0;
   maxInv[DeployableTurret] = 0;
};

function BadBot::spawn(%name, %startPos)
{
   %bot = new AIPlayer(%name) {
      dataBlock = BadBotData; 
      class = "BadBot";
   };
   
   if(%name !$= "")
      %bot.setShapeName(%name);
   
   if(%startPos $= "")
   {
      %spawnPoint = pickPlayerSpawnPoint(PlayerDropPoints);
      if(isObject(%spawnPoint))
         %startPos = %spawnPoint.getPosition();
   }
   
   %bot.setPosition(%startPos);
   
   %bot.tetherPoint = %startPos;
   
   return %bot;  
   //%bot.allowSprinting(false);
}

// override getMuzzleVector so that the bots aim at where they are looking
function BadBot::getMuzzleVector(%this, %slot)
{
   return %this.getEyeVector();
}

function BadBot::setBehavior(%this, %tree)
{
   if(isObject(%this.behaviorTree))
      %this.behaviorTree.rootNode = %tree;
   else      
      %this.behaviorTree = BehaviorTreeManager.createTree(%this, %tree); 
}

function BadBot::clearBehavior(%this)
{
   if(isObject(%this.behaviorTree))
      %this.behaviorTree.clear();
}

function BadBotData::onAdd(%data, %obj)
{
   game.loadout(%obj);
   if(getRandom(0,1))
      %obj.cycleWeapon("next");
}

function BadBotData::onRemove(%data, %obj)
{
   if(isObject(%obj.behaviorTree))
      %obj.behaviorTree.delete();
}

function botMatch(%numBots)
{
   if(!isObject(BotSet))
   {
      pushInstantGroup();
      new SimSet(BotSet);
      popInstantGroup();
   }

   %numActiveBots = 0;
   foreach(%bot in BotSet)
   {
      if(%bot.getState() !$= "Dead")
         %numActiveBots ++;
   }
   
   if(%numActiveBots < %numBots)
   {
      %spawnpoint = PatrolPath.getRandom();
      %bot = BadBot::spawn("Bot" @ getRandom(100000), %spawnpoint.position);
      %bot.tetherpoint = %bot.position;
      %bot.setbehavior(t2);
      BotSet.add(%bot);
   }
   
   $botSchedule = schedule(100, 0, botMatch, %numBots);
}

function cancelBotmatch()
{
   cancel($botSchedule);
   while(BotSet.getCount() > 0)
      BotSet.getObject(0).delete();
}

//==============================Movement=======================================

function BadBot::moveTo(%this, %dest, %slowDown)
{
   %pos = isObject(%dest) ? %dest.getPosition() : %dest;
   %this.setMoveDestination(%pos, %slowDown);
   %this.atDestination = false;
}

function BadBotData::onReachDestination(%data, %obj)
{
   %obj.atDestination = true;
}

// get the index of the closest node on the specified path
function BadBot::getClosestNodeOnPath(%this, %path)
{
   if(isObject(%path) && %path.isMemberOfClass("SimSet") && (%numNodes = %path.getCount()) > 0)
   {
      %bestNode = 0;
      %bestDist = VectorDist(%path.getObject(%bestNode).position, %this.position);
      
      for(%i=1; %i < %numNodes; %i++)
      {
         %node = %path.getObject(%i);
         %dist = VectorDist(%node.position, %this.position);
         
         if(%dist < %bestDist)
         {
            %bestNode = %i;
            %bestDist = %dist;  
         }
      }
      
      return %bestNode;
   }
   return -1;
}

//=============================Misc Bot Cmds===================================
function BadBot::say(%this, %message)
{
   chatMessageAll(%this, '\c3%1: %2', %this.getShapeName(), %message);  
}


//=============================Global Utility==================================
function RandomPointOnCircle(%center, %radius)
{
   %randVec = (getRandom() - 0.5) SPC (getRandom() - 0.5) SPC "0";
   %randVec = VectorNormalize(%randVec);
   %randVec = VectorScale(%randVec, %radius);
   return VectorAdd(%center, %randVec);  
}


//==============================================================================
// wander behavior task
//==============================================================================

function wanderTask::onEnter(%this, %obj)
{
   //echo("wanderTask::onEnter");
   %obj.clearAim();
   %basePoint = %obj.tetherPoint !$= "" ? %obj.tetherPoint : %obj.position;
   %obj.moveTo(RandomPointOnCircle(%basePoint, 10));
   %obj.atDestination = false;   
}

function wanderTask::behavior(%this, %obj)
{
//   echo("wanderTask::behavior");
   if(!%obj.atDestination)
      return RUNNING;
   
   return SUCCESS;
}


//==============================================================================
// Move to closest node task
//==============================================================================

function moveToClosestNodeTask::precondition(%this, %obj)
{
   return isObject(%obj.path);  
}

function moveToClosestNodeTask::onEnter(%this, %obj)
{
   %obj.clearAim();
   %obj.currentNode = %obj.getClosestNodeOnPath(%obj.path);
   %obj.moveToNextNode();
   %obj.atDestination = false;  
}

function moveToClosestNodeTask::behavior(%this, %obj)
{
   if(%obj.atDestination)
      return SUCCESS;
   
   return RUNNING;
}

//==============================================================================
// Patrol behavior task
//==============================================================================

function patrolTask::precondition(%this, %obj)
{
//   echo("patrolTask::precondition");
   return isObject(%obj.path);
}

function patrolTask::onEnter(%this, %obj)
{
//   echo("patrolTask::onEnter");
   %obj.clearAim();
   %obj.currentNode = %obj.getClosestNodeOnPath(%obj.path);
   %obj.atDestination = true;
}

function patrolTask::behavior(%this, %obj)
{
//   echo("patrolTask::behavior");
   if(%obj.atDestination)
   {
      %obj.moveToNextNode();
      %obj.atDestination = 0;
   }
   return RUNNING;
}

//=============================================================================
// findHealth task
//=============================================================================
function findHealthTask::behavior(%this, %obj)
{
   %bestDist = 9999;
   %bestItem = -1;
   %db = %obj.dataBlock;
   
   initContainerRadiusSearch( %obj.position, %db.findItemRange, %db.itemObjectTypes );
   while ( (%item = containerSearchNext()) != 0 )
   {
      if(%item.dataBlock.category !$= "Health" || !%item.isEnabled() || %item.isHidden())
         continue;
      
      if(%obj.checkInFov(%item, %db.visionFov))
      {
         %dist = VectorDist(%obj.position, %item.position);
         if(%dist < %bestDist)
         {
            %bestItem = %item;
            %bestDist = %dist;
         }
      }
   }
   
   %obj.targetItem = %bestItem;
   
   return isObject(%obj.targetItem) ? SUCCESS : FAILURE;
}

//=============================================================================
// getHealth task
//=============================================================================
function getHealthTask::precondition(%this, %obj)
{
   return (isObject(%obj.targetItem) && %obj.targetItem.isEnabled() && !%obj.targetItem.isHidden());  
}

function getHealthTask::onEnter(%this, %obj)
{
   %obj.moveTo(%obj.targetItem.position);  
}

function getHealthTask::behavior(%this, %obj)
{
   if(!%obj.atDestination)
      return RUNNING;
   
   return SUCCESS;
}

//=============================================================================
// scanForTarget task
//=============================================================================
function pickTargetTask::precondition(%this, %obj)
{
   // decide if we should pick a new target or keep the old one
   return !(isObject(%obj.targetObject) && VectorDist(%obj, %obj.targetObject) <= %obj.dataBlock.visionRange &&
      getRandom() > %obj.dataBlock.switchTargetProbability);
}

function pickTargetTask::behavior(%this, %obj)
{
   %bestDist = 9999;
   %bestTarget = -1;
   %db = %obj.dataBlock;
   
   initContainerRadiusSearch( %obj.position, %db.VisionRange, %db.targetObjectTypes );
   while ( (%target = containerSearchNext()) != 0 )
   {
      if(%target == %obj || !%target.isEnabled() || %target.isGod)
         continue;
      
      if(%obj.checkInFov(%target, %db.visionFov))
      {
         %dist = VectorDist(%obj.position, %target.position);
         if(%dist < %bestDist)
         {
            %bestTarget = %target;
            %bestDist = %dist;
         }
      }
   }
   
   %obj.targetObject = %bestTarget;
}

//=============================================================================
// aimAtTargetTask
//=============================================================================
function aimAtTargetTask::precondition(%this, %obj)
{
   return isObject(%obj.targetObject);
}

function aimAtTargetTask::behavior(%this, %obj)
{
   %targetPos = %obj.targetObject.getWorldBoxCenter();
   %weaponImage = %obj.getMountedImage($WeaponSlot);
   %projectile = %weaponImage.projectile;
   %correction = "0 0 1";
   if(isObject(%projectile))
   {
      // simple target leading
      %targetDist = VectorDist(%targetPos, %obj.position);
      %bulletVel = %projectile.muzzleVelocity;
      %correction = VectorAdd(%correction, VectorScale( %targetVel, (%targetDist / %bulletVel) ));
   }
   %obj.setAimObject(%obj.targetObject, %correction);
}

//=============================================================================
// shootAtTargetTask
//=============================================================================
function shootAtTargetTask::precondition(%this, %obj)
{
   return isObject(%obj.targetObject) && 
          %obj.checkInLos(%obj.targetObject) && 
          VectorDot(VectorNormalize(VectorSub(%obj.getAimLocation(), %obj.position)), %obj.getForwardVector()) > 0.9 &&
          %obj.getImageAmmo($WeaponSlot);  
}

function shootAtTargetTask::behavior(%this, %obj)
{
   %obj.setMoveTrigger($player::imageTrigger0);
}


//=============================================================================
// combatMoveTask
//=============================================================================
function combatMoveTask::behavior(%this, %obj)
{
   %image = %obj.getMountedImage($WeaponSlot);
   %db = %obj.getDatablock();
   %optimalRange = %db.optimalRange[%image.item.description];
   %currentRange = VectorDist(%obj.position, %obj.targetObject.position);
   %rangeDelta = %currentRange - %optimalRange;

   %moveVec = "0 0 0";
   %fwd = %obj.getForwardVector();
   %right = %obj.getRightVector();
   
   // forward / back
   if(mAbs(%rangeDelta) > %db.rangeTolerance)
      %moveVec = VectorScale(%fwd, %rangeDelta);
   
   // side
   %moveVec = VectorAdd(%moveVec, VectorScale(%right, 5 * (getRandom(0,2) - 1)));
      
   %obj.moveTo(VectorAdd(%obj.position, %moveVec));
}