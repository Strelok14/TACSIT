#ifndef TACID_TELEMETRY_SECURITY_H
#define TACID_TELEMETRY_SECURITY_H

#include <stdint.h>
#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

#define TACID_MAX_DISTANCES 8
#define TACID_HMAC_SIZE 32
#define TACID_BACKLOG_CAPACITY 128

typedef struct {
    uint8_t anchor_id;
    float distance_m;
    int8_t rssi;
} tacid_distance_t;

typedef struct {
    uint32_t beacon_id;
    uint32_t sequence;
    int64_t timestamp_ms;
    uint8_t key_version;
    uint8_t count;
    tacid_distance_t distances[TACID_MAX_DISTANCES];
    uint8_t hmac[TACID_HMAC_SIZE];
} tacid_packet_t;

typedef struct {
    tacid_packet_t items[TACID_BACKLOG_CAPACITY];
    uint16_t head;
    uint16_t tail;
    uint16_t size;
} tacid_packet_ring_t;

void tacid_ring_init(tacid_packet_ring_t* ring);
bool tacid_ring_push(tacid_packet_ring_t* ring, const tacid_packet_t* packet);
bool tacid_ring_pop(tacid_packet_ring_t* ring, tacid_packet_t* packet);

void tacid_hmac_sha256(
    const uint8_t* key,
    uint32_t key_len,
    const uint8_t* data,
    uint32_t data_len,
    uint8_t out[TACID_HMAC_SIZE]);

uint32_t tacid_build_canonical_payload(const tacid_packet_t* packet, char* out, uint32_t out_size);

#ifdef __cplusplus
}
#endif

#endif
