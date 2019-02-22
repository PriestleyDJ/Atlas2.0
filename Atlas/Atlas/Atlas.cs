using RLBotDotNet;

using System;
using System.Numerics;
using System.Diagnostics;
using System.Windows.Media;
using System.Threading.Tasks;

namespace RLBotCSharpExample
{
    // We want to Atlas to derive from "Bot", and then implement its abstract methods.
    class Atlas : Bot
    {

        // This string is simply for rendering which state Atlas is in, it doesn't affect its play.
        private String activeState = "";

        // Keeps track of Atlas' goals, for quick-chat purposes
        private int goals = 0;

        // For handling dodges;
        private Stopwatch dodgeWatch = new Stopwatch();

        // We want the constructor for ExampleBot to extend from Bot.
        public Atlas(string botName, int botTeam, int botIndex) : base(botName, botTeam, botIndex)
        {
            dodgeWatch.Reset();
            dodgeWatch.Start();
        }

        public override Controller GetOutput(rlbot.flat.GameTickPacket gameTickPacket)
        {
            // This controller object will be returned at the end of the method.
            // This controller will contain all the inputs that we want the bot to perform.
            Controller controller = new Controller();

            // Wrap gameTickPacket retrieving in a try-catch so that the bot doesn't crash whenever a value isn't present.
            // A value may not be present if it was not sent.
            // These are nullables so trying to get them when they're null will cause errors, therefore we wrap in try-catch.
            try
            {
                // Start printing for this frame.
                Console.Write(this.index + ": ");

                // Store the required data from the gameTickPacket.
                Vector3 ballLocation = fromFramework(gameTickPacket.Ball.Value.Physics.Value.Location.Value);
                Vector3 ballVelocity = fromFramework(gameTickPacket.Ball.Value.Physics.Value.Velocity.Value);
                Vector3 carLocation = fromFramework(gameTickPacket.Players(this.index).Value.Physics.Value.Location.Value);
                Vector3 carVelocity = fromFramework(gameTickPacket.Players(this.index).Value.Physics.Value.Velocity.Value);
                rlbot.flat.Rotator carRotation = gameTickPacket.Players(this.index).Value.Physics.Value.Rotation.Value;
                Boolean wheelContact = gameTickPacket.Players(this.index).Value.HasWheelContact;
                int team = gameTickPacket.Players(this.index).Value.Team;

                // Wildfire?
                Boolean teammate = hasTeammate(gameTickPacket);
                rlbot.flat.PlayerInfo wildfire = getTeammate(gameTickPacket);
                double wildfireDistanceBall = (teammate ? getDistance2D(wildfire.Physics.Value.Location.Value.X, ballLocation.X, wildfire.Physics.Value.Location.Value.Y, ballLocation.Y) : Double.MaxValue);

                //Quick-chat
                int goalsScored = gameTickPacket.Players(this.index).Value.ScoreInfo.Value.Goals;
                if(goalsScored > this.goals)
                {
                    SendQuickChatFromAgent(false, rlbot.flat.QuickChatSelection.Compliments_NiceOne);
                    Task.Delay(750).ContinueWith(t => SendQuickChatFromAgent(false, rlbot.flat.QuickChatSelection.Compliments_Thanks));
                    Task.Delay(1500).ContinueWith(t => SendQuickChatFromAgent(false, rlbot.flat.QuickChatSelection.Apologies_NoProblem));
                }
                this.goals = goalsScored;

                // Get the ball prediction data.
                rlbot.flat.BallPrediction prediction = GetBallPrediction();

                // Determine which way we are shooting (positive = blue, negative = orange).
                int teamSign = (team == 0 ? 1 : -1);
                Boolean wrongSide = carLocation.Y * teamSign > ballLocation.Y * teamSign;

                // Determine where the goals are.
                Vector3 enemyGoal = new Vector3(0F, 5120F * teamSign, 0F);
                Vector3 homeGoal = new Vector3(0F, -5120F * teamSign, 0F);
                                                                                                                                        
                // Make a dodge boolean, this'll come in handy later.
                Boolean dodge = false;

                // Calculate the distance from the car to the ball.
                var distanceToBall = getDistance2D(carLocation.X, ballLocation.X, carLocation.Y, ballLocation.Y);
                Console.Write((int)distanceToBall + " = ball distance");

                // The earliest point we can hit the ball on its predicted path.
                Vector3 hitPoint = getHitPoint(gameTickPacket, prediction);

                // Kickoff.
                Boolean kickoff = (ballLocation.X == 0 && ballLocation.Y == 0 && ballVelocity.X == 0 && ballVelocity.Y == 0 && ballVelocity.Z == 0);
                if (kickoff)
                {
                    activeState = "Kickoff";

                    if (wildfireDistanceBall < distanceToBall && Math.Abs(carLocation.Y) > 3000)
                    {
                        controller.Throttle = Math.Sign(Math.Abs(carLocation.Y) - 5120);
                        controller.Boost = false;
                        controller.Handbrake = false;
                    }
                    else
                    {
                        Vector3 targetLocation = new Vector3(0, Math.Abs(carLocation.Y) > 3600 ? carLocation.Y + teamSign * 900 : 0, 0);
                        controller = driveToLocation(gameTickPacket, controller, targetLocation);
                        controller.Boost = true;
                        controller.Handbrake = false;
                        dodge = Math.Abs(controller.Steer) < 0.4F && distanceToBall < (carLocation.X == 0 ? 3000 : 2850);
                    }
                }
                else if (Math.Abs(ballLocation.X) > 1800 && wildfireDistanceBall < distanceToBall - 800 && teamSign * ballLocation.Y > -3500 && teammate)
                {
                    // Recieve the pass!
                    activeState = "Lurking";
                    Vector3 targetLocation = new Vector3(-ballLocation.X / 3, enemyGoal.Y - (enemyGoal.Y - ballLocation.Y) * 2, 0);
                    controller = driveToLocation(gameTickPacket, controller, targetLocation);
                    dodge = (distanceToBall > 3500 && !gameTickPacket.Players(this.index).Value.IsSupersonic && gameTickPacket.Players(this.index).Value.Boost < 10);
                }
                else
                {
                    Boolean grabBoost = (distanceToBall > (teammate ? 2750 : 3750) && gameTickPacket.Players(this.index).Value.Boost < 40 && !wrongSide);
                    Vector3? boost = (grabBoost ? getClosestBoost(gameTickPacket, carLocation) : null);
                    if (boost != null)
                    {
                        //Grab boost
                        activeState = "Boost";
                        controller = driveToLocation(gameTickPacket, controller, (Vector3)boost);
                        controller.Boost = (Vector3.Distance((Vector3)boost, carLocation) > 2000);
                    }
                    else if (ballLocation.Z < (distanceToBall < 400 ? 140 : 250))
                    {
                        // Defending.                    
                        double defendingThreshold = (teammate ? 2.5D : 3.25D);
                        double ballAngle = correctAngle(Math.Atan2(ballLocation.Y - carLocation.Y, ballLocation.X - carLocation.X) - carRotation.Yaw);
                        double enemyGoalAngle = correctAngle(Math.Atan2(enemyGoal.Y - carLocation.Y, enemyGoal.X - carLocation.X) - carRotation.Yaw);
                        if (Math.Abs(ballAngle) + Math.Abs(enemyGoalAngle) > defendingThreshold && (Math.Abs(ballVelocity.Y) < 1200 || wrongSide) && distanceToBall > 800)
                        {
                            activeState = "Defending";
                            controller = driveToLocation(gameTickPacket, controller, getDistance2D(carLocation, homeGoal) < 1250 || teamSign * carLocation.Y < -5120 ? ballLocation : homeGoal);
                            dodge = Math.Abs(controller.Steer) < 0.2F && teamSign * carLocation.Y > -3000 && (gameTickPacket.Players(this.index).Value.Boost < 10 || !wrongSide);
                            controller.Boost = (controller.Boost && wrongSide);
                        }
                        else
                        {
                            // Attacking.
                            activeState = "Attacking";
                            
                            // Get the target location so we can shoot the ball towards the opponent's goal.
                            double distance = getDistance2D(hitPoint, carLocation);
                            Vector3 carToHitPoint = Vector3.Subtract(hitPoint, carLocation);
                            double offset = Math.Max(0, Math.Min(0.28, 0.03 * Math.Abs(carToHitPoint.Y) / Math.Abs(carToHitPoint.X)));
                            Console.Write(", " + (float)offset + " = offset");

                            Vector3 goalToHitPoint = Vector3.Subtract(enemyGoal, hitPoint);
                            Vector3 targetLocation = Vector3.Add(hitPoint, Vector3.Multiply(Vector3.Normalize(goalToHitPoint), (float)-(92.75 + distance * offset)));

                            Renderer.DrawLine3D(Color.FromRgb(100, 255, 200), hitPoint, targetLocation);
                            Renderer.DrawLine3D(Color.FromRgb(100, 255, 200), carLocation, targetLocation);
                            Renderer.DrawLine3D(Color.FromRgb(100, 255, 200), carLocation, hitPoint);

                            controller = driveToLocation(gameTickPacket, controller, targetLocation);

                            // Two conditions for dodging when attacking:
                            // 1: to get closer to the ball.
                            // 2: to hit the ball.
                            dodge = false;
                            if (Math.Abs(controller.Steer) < 0.2F && gameTickPacket.Players(this.index).Value.Boost < 10)
                            {
                                if (distanceToBall > 2400 && !gameTickPacket.Players(this.index).Value.IsSupersonic)
                                {
                                    dodge = true;
                                }
                                else if (distanceToBall < 380 && ballLocation.Z < 180 && Math.Abs(ballAngle - enemyGoalAngle) < 0.4 && ballVelocity.Length() > 1500 && Math.Abs(ballAngle) < 0.3)
                                {
                                    // Towards the ball.
                                    dodge = true;
                                    controller.Steer = (float)ballAngle;
                                }
                            }
                        }
                    }
                    else
                    {
                        // Catching the ball.
                        activeState = "Catching";

                        // Determine the time the ball will take to touch the ground
                        double u = ballVelocity.Z;
                        double a = -650;
                        double s = -(ballLocation.Z - 92.75);
                        double time = (-u - Math.Sqrt(Math.Pow(u, 2) + 2 * a * s)) / a;
                        Console.Write(", " + (float)time + " = time");

                        Vector3 bounceLocation = getBounceLocation(prediction);

                        // Add an offset so we dribble towards the enemy goal.
                        Vector3 bounceOffset = Vector3.Multiply(Vector3.Normalize(Vector3.Subtract(enemyGoal, bounceLocation)), -60);
                        bounceLocation = Vector3.Add(bounceLocation, bounceOffset);

                        controller = driveToLocationInTime(gameTickPacket, controller, bounceLocation, time);

                        if (time < 3.2 && getDistance2D(bounceLocation, carLocation) < 180 && Math.Abs(bounceLocation.X) < 800 && Math.Abs(bounceLocation.Y) > 4850 && teamSign * carVelocity.Y >= 0)
                        {
                            // Jump when the ball is near the goal.
                            dodge = false;
                            controller.Jump = DateTimeOffset.Now.ToUnixTimeMilliseconds() % 500 > 100;
                        }
                        else
                        {
                            dodge = gameTickPacket.Players(this.index).Value.Boost < 1 && getDistance2D(bounceLocation, carLocation) > 2900;
                        }
                    }
                }
                Console.Write(", " + (float)controller.Throttle + " = throttle");
                Console.Write(", " + (float)controller.Steer + " = steer");

                // Land on wheels.
                if (carLocation.Z > 200 && !isDodging(gameTickPacket))
                {
                    activeState = "Recovery";
                    float proportion = 0.8F;
                    controller.Roll = (float)carRotation.Roll * -proportion;
                    controller.Pitch = (float)carRotation.Pitch * -proportion;
                    controller.Boost = false;
                }

                // Handles dodging.
                Console.Write(", " + (dodgeWatch.ElapsedMilliseconds / 1000F) + "s dodge");
                if (isDodging(gameTickPacket))
                {
                    // Get the controller required for the dodge.
                    /** activeState = "Dodge"; */
                    controller = getDodgeOutput(controller, controller.Steer);
                }
                else if (dodge && canDodge(gameTickPacket) && wheelContact)
                {
                    // Begin a new dodge.                    
                    dodgeWatch.Restart();
                }

                // Render the active state.
                Renderer.DrawString3D(activeState, (team == 0 ? Color.FromRgb(0, 191, 255) : Color.FromRgb(255, 165, 0)), carLocation, 2, 2);

                // End the line printed this frame.
                Console.WriteLine();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
            }

            return clampControlValues(controller);
        }

        // Corrects the angle.
        private static double correctAngle(double botFrontToTargetAngle)
        {

            if (botFrontToTargetAngle < -Math.PI) botFrontToTargetAngle += 2 * Math.PI;
            if (botFrontToTargetAngle > Math.PI) botFrontToTargetAngle -= 2 * Math.PI;
            return botFrontToTargetAngle;
        }

        // Get the 2D distance between two points.
        public double getDistance2D(double x1, double x2, double y1, double y2)
        {
            return Math.Sqrt(Math.Pow((x2 - x1), 2) + Math.Pow((y2 - y1), 2));
        }

        // Get the 2D distance between two vectors.
        public double getDistance2D(Vector3 pointA, Vector3 pointB)
        {
            return getDistance2D(pointA.X, pointB.X, pointA.Y, pointB.Y);
        }

        // Get the size of a 2D vector.
        public double magnitude2D(Vector3 vector)
        {
            return Math.Sqrt(Math.Pow((vector.X - vector.X), 2) + Math.Pow((vector.Y - vector.Y), 2));
        }

        //This is the method that changes the controller so that it allows the bot to perform a dodge.
        private Controller getDodgeOutput(Controller controller, double steer)
        {
            if (dodgeWatch.ElapsedMilliseconds <= 120)
            {
                controller.Jump = (dodgeWatch.ElapsedMilliseconds <= 80);
                controller.Yaw = 0;
                controller.Pitch = 0;
            }
            else if (dodgeWatch.ElapsedMilliseconds <= 250)
            {
                controller.Jump = true;
                controller.Yaw = (float)-Math.Sin(steer);
                controller.Pitch = (float)-Math.Cos(steer);
            }
            else if (dodgeWatch.ElapsedMilliseconds <= 1000)
            {
                controller.Jump = false;
                controller.Yaw = 0;
                controller.Pitch = 0;
            }
            if(dodgeWatch.ElapsedMilliseconds <= 800) controller.Boost = false;

            return controller;
        }

        // Tells us whether the bot is dodging or not.
        private Boolean isDodging(rlbot.flat.GameTickPacket gameTickPacket)
        {
            return dodgeWatch.ElapsedMilliseconds <= (gameTickPacket.Players(this.index).Value.HasWheelContact ? 600 : 1000);
        }

        // Tells us whether the bot is eligible to perform a dodge or not.
        private Boolean canDodge(rlbot.flat.GameTickPacket gameTickPacket)
        {
            return dodgeWatch.ElapsedMilliseconds >= 2000 && gameTickPacket.Players(this.index).Value.HasWheelContact;
        }

        private Vector3 fromFramework(rlbot.flat.Vector3 v)
        {
            return new Vector3(v.X, v.Y, v.Z);
        }
        
        // Returns the location of where the ball will first hit the ground
        private Vector3 getBounceLocation(rlbot.flat.BallPrediction prediction)
        {
            for (int i = 0; i < prediction.SlicesLength; i++)
            {
                Vector3 point = fromFramework(prediction.Slices(i).Value.Physics.Value.Location.Value);                
                if(point.Z < 125)
                {
                    renderPrediction(prediction, 0, i, System.Windows.Media.Color.FromRgb(255, 0, 255));
                    return point;
                }               
            }
            return fromFramework(prediction.Slices(0).Value.Physics.Value.Location.Value);
        }

        private Controller driveToLocation(rlbot.flat.GameTickPacket gameTickPacket, Controller controller, Vector3 location)
        {
            Vector3 carLocation = fromFramework(gameTickPacket.Players(this.index).Value.Physics.Value.Location.Value);
            rlbot.flat.Rotator carRotation = gameTickPacket.Players(this.index).Value.Physics.Value.Rotation.Value;

            // Stuck in goal.
            if(Math.Abs(carLocation.Y) > 5120)
            {
                location = new Vector3(Math.Min(800, Math.Max(-800, location.X)), location.Y, location.Z);
            }

            if (carLocation.Z < 120)
            {
                double botToLocationAngle = Math.Atan2(location.Y - carLocation.Y, location.X - carLocation.X);
                double botFrontToLocationAngle = correctAngle(botToLocationAngle - carRotation.Yaw);

                float steer = (float)botFrontToLocationAngle * 2.5F;
                controller.Steer = steer;
                
                controller.Boost = (Math.Abs(steer) < 0.12F && gameTickPacket.Players(this.index).Value.HasWheelContact && !gameTickPacket.Players(this.index).Value.IsSupersonic);
                controller.Handbrake = (Math.Abs(steer) > 2.8F && gameTickPacket.Players(this.index).Value.HasWheelContact);
            }
            else
            {                
                controller.Boost = false;
                controller.Handbrake = false;
                controller.Steer = carRotation.Roll * 10;
            }
            controller.Throttle = 1F;

            return controller;
        }

        private Controller driveToLocationInTime(rlbot.flat.GameTickPacket gameTickPacket, Controller controller, Vector3 location, double time)
        {
            Vector3 carLocation = fromFramework(gameTickPacket.Players(this.index).Value.Physics.Value.Location.Value);
            Vector3 carVelocity = fromFramework(gameTickPacket.Players(this.index).Value.Physics.Value.Velocity.Value);

            // Get the default driving controller
            controller = driveToLocation(gameTickPacket, controller, location);

            // Handling the speed
            double distance = getDistance2D(carLocation.X, location.X, carLocation.Y, location.Y);
            double targetSpeed = (distance / time);
            double currentSpeed = carVelocity.Length();

            if (targetSpeed > currentSpeed)
            {
                controller.Throttle = 1;
                controller.Boost = ((targetSpeed > 1410 || targetSpeed > currentSpeed + 800) && carLocation.Z < 30 && Math.Abs(controller.Steer) < 0.5);
            }
            else
            {
                controller.Boost = false;
                if (targetSpeed < currentSpeed - 400)
                {
                    controller.Throttle = -1;
                }
                else
                {
                    controller.Throttle = 0;
                }
            }

            return controller;
        }

        // Returns a hittable point on the ball.
        private Vector3 getHitPoint(rlbot.flat.GameTickPacket gameTickPacket, rlbot.flat.BallPrediction prediction)
        {
            Vector3 carLocation = fromFramework(gameTickPacket.Players(this.index).Value.Physics.Value.Location.Value);
            double u = fromFramework(gameTickPacket.Players(this.index).Value.Physics.Value.Velocity.Value).Length();

            // Estimate the maximum velocity.           
            double maxV = Math.Max(1410, Math.Min(2300, u + 150 * gameTickPacket.Players(this.index).Value.Boost));

            for (int i = 0; i < prediction.SlicesLength; i++)
            {
                Vector3 point = fromFramework(prediction.Slices(i).Value.Physics.Value.Location.Value);

                double s = Vector3.Distance(point, carLocation) - 92.75;
                double t = (double)i / 60D;
                double v = 2D * (s / t) - u;
                double a = (Math.Pow(v, 2) - Math.Pow(u, 2)) / (2 * s);

                if (v <= maxV && a < 1700) // Approximate max acceleration.
                {
                    renderPrediction(prediction, 0, i, System.Windows.Media.Color.FromRgb(255, 255, 255));
                    return point;
                }
            }
            return fromFramework(prediction.Slices(0).Value.Physics.Value.Location.Value);
        }

        // Renders the prediction up to a certain point.
        private void renderPrediction(rlbot.flat.BallPrediction prediction, int start, int end, System.Windows.Media.Color colour)
        {
            for (int i = Math.Max(1, start); i < Math.Min(prediction.SlicesLength, end); i++)
            {
                Vector3 pointA = fromFramework(prediction.Slices(i - 1).Value.Physics.Value.Location.Value);
                Vector3 pointB = fromFramework(prediction.Slices(i).Value.Physics.Value.Location.Value);
                Renderer.DrawLine3D(colour, pointA, pointB);
            }
        }

        private float clampSign(float value)
        {
            return Math.Max(-1, Math.Min(1, value));
        }

        // Clamp the values in the controller as to avoid "invalid value" errors.
        private Controller clampControlValues(Controller controller)
        {
            controller.Pitch = clampSign(controller.Pitch);
            controller.Yaw = clampSign(controller.Yaw);
            controller.Roll = clampSign(controller.Roll);
            controller.Steer = clampSign(controller.Steer);
            controller.Throttle = clampSign(controller.Throttle);
            return controller;
        }

        private Boolean hasTeammate(rlbot.flat.GameTickPacket gameTickPacket)
        {
            for (int i = 0; i < gameTickPacket.PlayersLength; i++)
            {
                if (i == this.index) continue; // This is Atlas!
                if (gameTickPacket.Players(i).Value.Team == this.team) return true;
            }
            return false; // No teammate.
        }

        private rlbot.flat.PlayerInfo getTeammate(rlbot.flat.GameTickPacket gameTickPacket)
        {
            for(int i = 0; i < gameTickPacket.PlayersLength; i++)
            {
                if (i == this.index) continue; // This is Atlas!
                if (gameTickPacket.Players(i).Value.Team == this.team)
                {
                    // Wildfire found!
                    return (rlbot.flat.PlayerInfo)gameTickPacket.Players(i); 
                }
            }
            return (rlbot.flat.PlayerInfo)gameTickPacket.Players(this.index); // No teammate.
        }

        private Vector3? getClosestBoost(rlbot.flat.GameTickPacket gameTickPacket, Vector3 carLocation)
        {
            Vector3? closest = null;
            double closestDistance = 0;
            for(int i = 0; i < gameTickPacket.BoostPadStatesLength; i++)
            {
                rlbot.flat.BoostPadState boostPadState = (rlbot.flat.BoostPadState)gameTickPacket.BoostPadStates(i);
                rlbot.flat.BoostPad boostPosition = (rlbot.flat.BoostPad)GetFieldInfo().BoostPads(i);
                double boostDistance = getDistance2D(carLocation.X, boostPosition.Location.Value.X, carLocation.Y, boostPosition.Location.Value.Y);

                if (boostPadState.IsActive && boostPosition.IsFullBoost && (closest == null || closestDistance > boostDistance))
                {
                    closestDistance = boostDistance;
                    closest = fromFramework((rlbot.flat.Vector3)boostPosition.Location);
                }
            }
            return closest;
        }

    }
}
