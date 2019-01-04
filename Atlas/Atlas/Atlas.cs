﻿using RLBotDotNet;

using System;
using System.Numerics;
using System.Diagnostics;
using System.Windows.Media;

namespace RLBotCSharpExample
{
    // We want to our bot to derive from Bot, and then implement its abstract methods.
    class Atlas : Bot
    {
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

                // Get the ball prediction data.
                rlbot.flat.BallPrediction prediction = GetBallPrediction();

                // Determine which way we are shooting (positive = blue, negative = orange).
                int teamSign = team == 0 ? 1 : -1;
                
                // Determine where the goals are.
                Vector3 enemyGoal = new Vector3(0F, 5120F * teamSign, 0F);
                Vector3 homeGoal = new Vector3(0F, -5120F * teamSign, 0F);
                                                                                                                                        
                // Make a dodge boolean, this'll come in handy later.
                Boolean dodge = false;

                // Calculate the distance from the car to the ball
                var distanceToBall = getDistance2D(carLocation.X, ballLocation.X, carLocation.Y, ballLocation.Y);
                Console.Write((int)distanceToBall + " = ball distance");

                // Kickoff.
                Boolean kickoff = (ballLocation.X == 0 && ballLocation.Y == 0 && ballVelocity.X == 0 && ballVelocity.Y == 0 && ballVelocity.Z == 0);
                if (kickoff)
                {
                    Vector3 targetLocation = new Vector3(0, Math.Abs(carLocation.Y) > 3600 ? carLocation.Y + teamSign * 900 : 0, 0);
                    controller = driveToLocation(gameTickPacket, controller, targetLocation);
                    controller.Boost = true;
                    controller.Handbrake = false;
                    dodge = Math.Abs(controller.Steer) < 0.4F && distanceToBall < (carLocation.X == 0 ? 3000 : 2850);
                }else if (ballLocation.Z < (distanceToBall < 500 ? 140 : 250))
                {
                    // Defending.
                    double defendingThreshold = 3.25D;
                    double ballAngle = correctAngle(Math.Atan2(ballLocation.Y - carLocation.Y, ballLocation.X - carLocation.X) - carRotation.Yaw);
                    double enemyGoalAngle = correctAngle(Math.Atan2(enemyGoal.Y - carLocation.Y, enemyGoal.X - carLocation.X) - carRotation.Yaw);
                    Boolean wrongSide = carLocation.Y * teamSign > ballLocation.Y * teamSign;
                    if (Math.Abs(ballAngle) + Math.Abs(enemyGoalAngle) > defendingThreshold && (Math.Abs(ballVelocity.Y) < 1200 || wrongSide))
                    {
                        controller = driveToLocation(gameTickPacket, controller, getDistance2D(carLocation, homeGoal) < 1250 || teamSign * carLocation.Y < -5120 ? ballLocation : homeGoal);
                        dodge = Math.Abs(controller.Steer) < 0.2F && teamSign * carLocation.Y > -3000 && (gameTickPacket.Players(this.index).Value.Boost < 10 || !wrongSide);
                        controller.Boost = controller.Boost && wrongSide;
                    }
                    else
                    {
                        // Attacking.
                        Vector3 hitPoint = getHitPoint(gameTickPacket, prediction);

                        // Get the target location so we can shoot the ball towards the opponent's goal.
                        double distance = getDistance2D(hitPoint, carLocation);
                        Vector3 carToHitPoint = Vector3.Subtract(hitPoint, carLocation);
                        double offset = Math.Max(0, Math.Min(0.31, 0.075 * Math.Abs(carToHitPoint.Y) / Math.Abs(carToHitPoint.X)));
                        Console.Write(", " + (float)offset + " = offset");
                        Vector3 goalToHitPoint = Vector3.Subtract(enemyGoal, hitPoint);
                        Vector3 targetLocation = Vector3.Add(hitPoint, Vector3.Multiply(Vector3.Normalize(goalToHitPoint), (float)(distance * -offset)));

                        controller = driveToLocation(gameTickPacket, controller, targetLocation);
                        dodge = Math.Abs(controller.Steer) < 0.2F && (distanceToBall > 2000 || (distanceToBall < 500 && ballLocation.Z < 180)) && gameTickPacket.Players(this.index).Value.Boost < 10;
                    }
                }
                else
                {
                    // Catching the ball.
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

                    if (time < 3 && getDistance2D(bounceLocation, carLocation) < 150 && Math.Abs(bounceLocation.X) < 700 && Math.Abs(bounceLocation.Y) > 4850)
                    {
                        dodge = false;
                        controller.Jump = DateTimeOffset.Now.ToUnixTimeMilliseconds() % 200 > 100;
                    }
                    else
                    {
                        dodge = gameTickPacket.Players(this.index).Value.Boost < 1 && getDistance2D(bounceLocation, carLocation) > 2500;
                    }
                }
                Console.Write(", " + (float)controller.Throttle + " = throttle");
                Console.Write(", " + (float)controller.Steer + " = steer");

                // Land on wheels.
                if (carLocation.Z > 200 && !isDodging())
                {
                    float proportion = 0.8F;
                    controller.Roll = (float)carRotation.Roll * -proportion;
                    controller.Pitch = (float)carRotation.Pitch * -proportion;
                }

                // Handles dodging.
                Console.Write(", " + (dodgeWatch.ElapsedMilliseconds / 1000F) + "s dodge");
                if (isDodging())
                {
                    // Get the controller required for the dodge.
                    controller = getDodgeOutput(controller, controller.Steer);
                }
                else if (dodge && canDodge(gameTickPacket) && wheelContact)
                {
                    // Begin a new dodge.                    
                    dodgeWatch.Restart();
                }

                // End the line printed this frame.
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
            controller.Boost = false;
            if (dodgeWatch.ElapsedMilliseconds <= 100)
            {
                controller.Jump = true;
                controller.Pitch = -1;
            }
            else if (dodgeWatch.ElapsedMilliseconds <= 225)
            {
                controller.Jump = false;
                controller.Pitch = -1;
            }
            else if (dodgeWatch.ElapsedMilliseconds <= 1000)
            {
                controller.Jump = true;
                controller.Yaw = -(float)Math.Sin(steer);
                controller.Pitch = (float)-Math.Cos(steer);
            }
            return controller;
        }

        // Tells us whether the bot is dodging or not.
        private Boolean isDodging()
        {
            return dodgeWatch.ElapsedMilliseconds <= 1000;
        }

        // Tells us whether the bot is eligible to perform a dodge or not.
        private Boolean canDodge(rlbot.flat.GameTickPacket gameTickPacket)
        {
            return dodgeWatch.ElapsedMilliseconds >= 2200 && gameTickPacket.Players(this.index).Value.HasWheelContact;
        }

        private Vector3 fromFramework(rlbot.flat.Vector3 vec)
        {
            return new Vector3(vec.X, vec.Y, vec.Z);
        }
        
        // Returns the location of where the ball will first hit the ground
        private Vector3 getBounceLocation(rlbot.flat.BallPrediction prediction)
        {
            for (int i = 0; i < prediction.SlicesLength; i++)
            {
                Vector3 point = fromFramework(prediction.Slices(i).Value.Physics.Value.Location.Value);                
                if(point.Z < 110)
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

                float steer = (float)botFrontToLocationAngle * 2F;
                controller.Steer = steer;
                
                controller.Boost = (Math.Abs(steer) < 0.12F && gameTickPacket.Players(this.index).Value.HasWheelContact);
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
            double targetSpeed = distance / time;
            double currentSpeed = carVelocity.Length();
            if (targetSpeed > currentSpeed)
            {
                controller.Throttle = 1;
                controller.Boost = targetSpeed > 1410 || targetSpeed > currentSpeed + 1000;
            }
            else
            {
                controller.Boost = false;
                if (targetSpeed < currentSpeed - 200)
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
                if (v <= maxV)
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

    }
}