package com.example.tacsit.ai;

import java.util.List;

public final class AiObservationBatchRequest {

    public final List<AiObservation> observations;

    public AiObservationBatchRequest(List<AiObservation> observations) {
        this.observations = observations;
    }
}
