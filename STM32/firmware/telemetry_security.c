#include "telemetry_security.h"

#include <string.h>
#include <stdio.h>

// Для STM32 в реальном проекте подключите mbedTLS или аппаратный SHA/HMAC.
// Здесь оставлен адаптерный интерфейс для встраивания в существующую прошивку.
extern void platform_hmac_sha256(
    const uint8_t* key,
    uint32_t key_len,
    const uint8_t* data,
    uint32_t data_len,
    uint8_t out[TACID_HMAC_SIZE]);

void tacid_ring_init(tacid_packet_ring_t* ring) {
    if (ring == NULL) {
        return;
    }
    memset(ring, 0, sizeof(*ring));
}

bool tacid_ring_push(tacid_packet_ring_t* ring, const tacid_packet_t* packet) {
    if (ring == NULL || packet == NULL) {
        return false;
    }

    // Кольцевой буфер: при переполнении вытесняем самый старый пакет.
    if (ring->size == TACID_BACKLOG_CAPACITY) {
        ring->tail = (uint16_t)((ring->tail + 1U) % TACID_BACKLOG_CAPACITY);
        ring->size--;
    }

    ring->items[ring->head] = *packet;
    ring->head = (uint16_t)((ring->head + 1U) % TACID_BACKLOG_CAPACITY);
    ring->size++;
    return true;
}

bool tacid_ring_pop(tacid_packet_ring_t* ring, tacid_packet_t* packet) {
    if (ring == NULL || packet == NULL || ring->size == 0) {
        return false;
    }

    *packet = ring->items[ring->tail];
    ring->tail = (uint16_t)((ring->tail + 1U) % TACID_BACKLOG_CAPACITY);
    ring->size--;
    return true;
}

void tacid_hmac_sha256(
    const uint8_t* key,
    uint32_t key_len,
    const uint8_t* data,
    uint32_t data_len,
    uint8_t out[TACID_HMAC_SIZE]) {
    platform_hmac_sha256(key, key_len, data, data_len, out);
}

uint32_t tacid_build_canonical_payload(const tacid_packet_t* packet, char* out, uint32_t out_size) {
    if (packet == NULL || out == NULL || out_size == 0) {
        return 0;
    }

    uint32_t used = 0;
    int n = snprintf(out, out_size, "%lu|%lu|%lld|",
                     (unsigned long)packet->beacon_id,
                     (unsigned long)packet->sequence,
                     (long long)packet->timestamp_ms);
    if (n < 0) {
        return 0;
    }

    used = (uint32_t)n;
    for (uint8_t i = 0; i < packet->count; i++) {
        if (used >= out_size) {
            return 0;
        }

        const tacid_distance_t* d = &packet->distances[i];
        n = snprintf(out + used, out_size - used, "%u:%.6f:%d;",
                     (unsigned)d->anchor_id,
                     (double)d->distance_m,
                     (int)d->rssi);
        if (n < 0) {
            return 0;
        }
        used += (uint32_t)n;
    }

    if (used >= out_size) {
        return 0;
    }
    return used;
}
