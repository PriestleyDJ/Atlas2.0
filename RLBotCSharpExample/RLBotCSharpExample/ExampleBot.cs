using System;
using RLBotDotNet;
using rlbot.flat;
using System.Diagnostics;
using System.Numerics;

namespace RLBotCSharpExample
{
    // We want to our bot to derive from Bot, and then implement its abstract methods.
    class ExampleBot : Bot
    {
        private Stopwatch kickoffWatch = new Stopwatch();
        private Stopwatch dodgeWatch = new Stopwatch();

        // We want the constructor for ExampleBot to extend from Bot.
        public ExampleBot(string botName, int botTeam, int botIndex) : base(botName, botTeam, botIndex)
        {
            kickoffWatch.Reset();
            kickoffWatch.Start();

            dodgeWatch.Reset();
            dodgeWatch.Start();
        }
        
        public override Controller GetOutput(GameTickPacket gameTickPacket)
        {
            // This controller object will be returned at the end of the method.
            // This controller will contain all the inputs that we want the bot to perform.
            Controller controller = new Controller();

            // Wrap gameTickPacket retrieving in a try-catch so that the bot doesn't crash whenever a value isn't present.
            // A value may not be present if it was not sent.
            // These are nullables so trying to get them when they're null will cause errors, therefore we wrap in try-catch.
            try
            {
                // Store the required data from the gameTickPacket.
                System.Numerics.Vector3 ballLocation = fromFramework(gameTickPacket.Ball.Value.Physics.Value.Location.Value);
                System.Numerics.Vector3 ballVelocity = fromFramework(gameTickPacket.Ball.Value.Physics.Value.Velocity.Value);
                System.Numerics.Vector3 carLocation = fromFramework(gameTickPacket.Players(this.index).Value.Physics.Value.Location.Value);
                Rotator carRotation = gameTickPacket.Players(this.index).Value.Physics.Value.Rotation.Value;

                //Determine where the opponent's goal is
                System.Numerics.Vector3 enemyGoal;
                if(gameTickPacket.Players(this.index).Value.Team == 0)
                {
                    // Blue team shooting towards the orange net.
                    enemyGoal = new System.Numerics.Vector3(0F, 5120F, 0F);
                }
                else
                {
                    // Orange team shooting towards the blue net.
                    enemyGoal = new System.Numerics.Vector3(0F, -5120F, 0F);
                }

                // Calculate the distance from the car to the ball
                var distanceToBall = getDistance2D(carLocation.X, ballLocation.X, carLocation.Y, ballLocation.Y);

                // Get the target location so we can shoot the ball towards the opponent's goal.
                System.Numerics.Vector3 goalToBall = System.Numerics.Vector3.Subtract(enemyGoal, ballLocation);
                Console.Write("[" + goalToBall.X + ", " + goalToBall.Y + "] = goalToBall");

                System.Numerics.Vector3 targetLocation = System.Numerics.Vector3.Add(ballLocation, System.Numerics.Vector3.Multiply(System.Numerics.Vector3.Normalize(goalToBall), (float)distanceToBall * 0.5F));
                Console.Write(", [" + targetLocation.X + ", " + targetLocation.Y + "] = targetLocation");

                // Calculate to get the angle from the front of the bot's car to the target location.
                double botToTargetAngle = Math.Atan2(targetLocation.Y - carLocation.Y, targetLocation.X - carLocation.X);
                double botFrontToTargetAngle = correctAngle(botToTargetAngle - carRotation.Yaw);

                // Decide which way to steer in order to get to the ball.
                float steer = (float)botFrontToTargetAngle * 2F;
                controller.Steer = steer;
                Console.Write(", " + steer + " = steer");

                // Change the throttle so the bot can move.
                //controller.Throttle = (float)Math.Cos(botFrontToTargetAngle);
                controller.Throttle = 1F;

                // Handles sliding
                //controller.Handbrake = (Math.Abs(steer) > 3.5 && carLocation.Z < 120);
                controller.Handbrake = false;

                // Handles boosting
                controller.Boost = (Math.Abs(steer) < 0.12F && carLocation.Z < 120);

                // Kickoff
                Boolean kickoff = (ballLocation.X == 0 && ballLocation.Y == 0 && ballVelocity.X == 0 && ballVelocity.Y == 0 && ballVelocity.Z == 0);
                if (kickoff)
                {
                    // Pause on kickoff for the first second.
                    // The other four seconds are the countdown.
                    if (kickoffWatch.ElapsedMilliseconds <= 5000)
                    {
                        controller.Boost = false;
                        controller.Throttle = 0;
                        Console.Write(", " + (kickoffWatch.ElapsedMilliseconds / 1000F) + "s kickoff");
                    }
                }
                else
                {
                    kickoffWatch.Stop();
                }

                // Handles dodging
                Console.Write(", " + (dodgeWatch.ElapsedMilliseconds / 1000F) + "s dodge");
                if (isDodging())
                {
                    // Get the controller required for the dodge.
                    controller = getDodgeOutput(controller, steer);
                }
                else if (Math.Abs(steer) < 0.2F && canDodge(gameTickPacket) && carLocation.Z < 120 && distanceToBall > 2000)
                {
                    // Begin a new dodge.
                    // This will dodge at any point when it's not kickoff, or any point during kickoff when we have no boost.
                    if(!kickoff || gameTickPacket.Players(this.index).Value.Boost == 0) dodgeWatch.Restart();
                }

                // End the line printed this frame
                Console.WriteLine();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
            }
            
            return controller;
        }

        // Corrects the angle.
        private static double correctAngle(double botFrontToTargetAngle)
        {
            
            if (botFrontToTargetAngle < -Math.PI) botFrontToTargetAngle += 2 * Math.PI;
            if (botFrontToTargetAngle > Math.PI) botFrontToTargetAngle -= 2 * Math.PI;
            return botFrontToTargetAngle;
        }

        // Get the 2D distance between two points
        public double getDistance2D(double x1, double x2, double y1, double y2)
        {
            return Math.Sqrt(Math.Pow((x2 - x1), 2) + Math.Pow((y2 - y1), 2));
        }

        // Get the size of a 2D vector
        public double magnitude2D(System.Numerics.Vector3 vector)
        {
            return Math.Sqrt(Math.Pow((vector.X - vector.X), 2) + Math.Pow((vector.Y - vector.Y), 2));
        }

        //This is the method that changes the controller so that it allows the bot to performa dodge.
        private Controller getDodgeOutput(Controller controller, double steer)
        {
            controller.Boost = false;
            if (dodgeWatch.ElapsedMilliseconds <= 150)
            {
                controller.Jump = true;
                controller.Pitch = -1;
            }
            else if (dodgeWatch.ElapsedMilliseconds <= 275)
            {
                controller.Jump = false;
                controller.Pitch = -1;
            }
            else if (dodgeWatch.ElapsedMilliseconds <= 1000)
            {
                controller.Jump = true;
                controller.Yaw = (float)Math.Sin(steer);
                controller.Pitch = (float)-Math.Cos(steer);
            }
            return controller;
        }

        // Tells us whether the bot is dodging or not
        private Boolean isDodging()
        {
            return dodgeWatch.ElapsedMilliseconds <= 1000;
        }

        // Tells us whether the bot is eligible to perform a dodge or not
        private Boolean canDodge(GameTickPacket gameTickPacket)
        {
            return dodgeWatch.ElapsedMilliseconds >= 2200 && gameTickPacket.Players(this.index).Value.HasWheelContact;
        }

        private System.Numerics.Vector3 fromFramework(rlbot.flat.Vector3 vec)
        {
            return new System.Numerics.Vector3(vec.X, vec.Y, vec.Z);
        }

        private System.Numerics.Vector3 clone(System.Numerics.Vector3 vec)
        {
            return new System.Numerics.Vector3(vec.X, vec.Y, vec.Z);
        }

    }
}
