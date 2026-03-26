# MustHaveIdea

## Goal
Enable each phone to estimate target direction and distance locally, then share compact observations so teammates can see the same target from different angles.

## Phone-side Observation Payload
- deviceId
- sessionId
- timestampMs
- classId
- confidence
- bboxCxPx, bboxCyPx, bboxWPx, bboxHPx
- frameWidth, frameHeight
- yawDeg, pitchDeg, rollDeg
- bearingDeg, elevationDeg
- rangeM (optional, heuristic for MVP)
- latitude, longitude, altitudeM (optional)
- quality

## MVP Math
1. Convert bbox center to camera angles via focal approximation.
2. Compute bearing/elevation by combining camera angle with device orientation.
3. Estimate distance for person class via apparent bbox height:
   - range ~= fy * personHeight / bboxHeight
4. Send observations at low rate (2-5 Hz) to reduce battery and traffic.

## Server-side Fusion (next step)
- Time-window association of observations.
- Triangulation by intersecting rays from 2+ observers.
- Kalman smoothing and confidence score.
- Broadcast fused target state to clients.

## Implementation Steps
1. Android: pose tracker (rotation vector + location).
2. Android: observation estimator from detections.
3. Android: upload observations endpoint client.
4. Server: endpoint + in-memory fusion service.
5. Map UI: show direction, range, uncertainty, source count.
